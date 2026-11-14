using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GraphiteCompanion.Services;

// Bounded producer queue and a single asynchronous disk writer. Rotation never deletes files.
public sealed class MetricsLogWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)};
    private readonly Channel<object> queue=Channel.CreateBounded<object>(new BoundedChannelOptions(16){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly long limit;
    private readonly Task worker;
    private readonly long started=Stopwatch.GetTimestamp();
    private long dropped;
    private volatile bool running=true;
    public string Folder {get;}
    public bool Running=>running;
    public string Status {get;private set;}="Журнал записывается";
    public event Action<string>? StateChanged;
    public MetricsLogWriter(string parent,int limitMb,object configuration)
    {
        limit=Math.Clamp(limitMb,10,2048)*1024L*1024;Folder=Path.Combine(Path.GetFullPath(parent),"SoundShot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(Folder);
        // Probe write access synchronously so the UI never reports a recording that could not start.
        File.WriteAllText(Path.Combine(Folder,"session.json"),JsonSerializer.Serialize(new{format="SoundShot metrics log",schema=1,startedUtc=DateTime.UtcNow,application=Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.OSArchitecture.ToString(),runtime=RuntimeInformation.FrameworkDescription,cpuThreads=Environment.ProcessorCount,sampleClock="UTC + monotonic elapsed milliseconds",frames="RTSS when available; otherwise PresentMon dominant swap chain. 1% low over 10 seconds is available with PresentMon only.",configuration,limitBytes=limit,units="Values retain the Unit field. Null means unavailable, not zero."},new JsonSerializerOptions{WriteIndented=true}));
        worker=Task.Run(WriteLoop);
    }
    public void Record(MetricSnapshot snapshot)
    {
        if(!running)return;
        object? diagnostics=null;try{using var process=Process.GetCurrentProcess();diagnostics=new{workingSetBytes=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,cpuTotalMs=process.TotalProcessorTime.TotalMilliseconds,handles=process.HandleCount,managedBytes=GC.GetTotalMemory(false),gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2)};}catch{}
        Enqueue(new{type="sample",utc=snapshot.Timestamp,elapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,collectionDurationMs=snapshot.CollectionDurationMs,status=snapshot.Status,available=snapshot.Values.Count(v=>v.Value.HasValue),diagnostics,metrics=snapshot.Values});
    }
    public void Event(string name,object? details=null)=>Enqueue(new{type="event",utc=DateTime.UtcNow,elapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,name,details});
    private void Enqueue(object entry){if(running&&!queue.Writer.TryWrite(entry))Interlocked.Increment(ref dropped);}
    private async Task WriteLoop()
    {
        FileStream? file=null;long total=0,partBytes=0;int part=0;DateTime lastFlush=DateTime.UtcNow;
        async Task<bool> Write(object value)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,JsonOptions)+"\n");if(total+bytes.Length>limit){Status="Достигнут лимит журнала. Запись остановлена; файлы сохранены.";return false;}
            if(file==null||partBytes+bytes.Length>20*1024*1024){if(file!=null)await file.DisposeAsync();file=new FileStream(Path.Combine(Folder,$"metrics-{++part:000}.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,16384,FileOptions.Asynchronous|FileOptions.SequentialScan);partBytes=0;}
            await file.WriteAsync(bytes);total+=bytes.Length;partBytes+=bytes.Length;if(DateTime.UtcNow-lastFlush>TimeSpan.FromSeconds(1)){await file.FlushAsync();lastFlush=DateTime.UtcNow;}return true;
        }
        try
        {
            total=new FileInfo(Path.Combine(Folder,"session.json")).Length;
            await foreach(var entry in queue.Reader.ReadAllAsync())
            {
                long lost=Interlocked.Exchange(ref dropped,0);if(lost>0&&!await Write(new{type="gap",utc=DateTime.UtcNow,reason="Очередь диска заполнена",droppedRecords=lost}))break;
                if(!await Write(entry))break;
            }
            if(Status=="Журнал записывается"&&await Write(new{type="end",utc=DateTime.UtcNow,elapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,droppedRecords=Interlocked.Read(ref dropped)}))Status="Журнал сохранён: "+Folder;
        }
        catch(Exception ex){Status="Запись журнала остановлена: "+ex.Message;}
        finally{running=false;queue.Writer.TryComplete();while(queue.Reader.TryRead(out _)){}if(file!=null)try{await file.DisposeAsync();}catch(Exception ex){Status="Не удалось завершить файл журнала: "+ex.Message;}StateChanged?.Invoke(Status);}
    }
    public async ValueTask DisposeAsync(){queue.Writer.TryComplete();await worker;}
}

