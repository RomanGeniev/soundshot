using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GraphiteCompanion.Services;

public sealed class FrameAccumulator
{
    private readonly Queue<(DateTime At,string Chain,double Ms)> frames=new();
    public void Add(DateTime at,string chain,double ms)
    {
        if(!double.IsFinite(ms)||ms<=0||ms>60000)return;
        frames.Enqueue((at,chain,ms));Trim(at);
    }
    private void Trim(DateTime now){while(frames.Count>6000||frames.Count>0&&now-frames.Peek().At>TimeSpan.FromSeconds(10))frames.Dequeue();}
    public (double? Fps,double? Time,double? Low) Read(DateTime now)
    {
        Trim(now);if(frames.Count==0||now-frames.Last().At>TimeSpan.FromSeconds(2))return(null,null,null);
        // A process can present several swap chains. Never sum their frame rates.
        var current=frames.Where(f=>now-f.At<=TimeSpan.FromSeconds(1)).GroupBy(f=>f.Chain).OrderByDescending(g=>g.Count()).FirstOrDefault();
        if(current==null)return(null,null,null);
        var chain=frames.Where(f=>f.Chain==current.Key);
        var recent=chain.Where(f=>now-f.At<=TimeSpan.FromSeconds(1)).Select(f=>f.Ms).ToArray();
        if(recent.Length==0)return(null,null,null);
        var all=chain.Select(f=>f.Ms).OrderByDescending(x=>x).ToArray();
        return(1000/recent.Average(),recent.Average(),all.Length>=100?1000/all.Take(Math.Max(1,(int)Math.Ceiling(all.Length*.01))).Average():null);
    }
    public static string[] Csv(string text)
    {
        var result=new List<string>();var cell=new StringBuilder();bool quote=false;
        for(int i=0;i<text.Length;i++){char c=text[i];if(c=='"'){if(quote&&i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else quote=!quote;}else if(c==','&&!quote){result.Add(cell.ToString());cell.Clear();}else cell.Append(c);}
        result.Add(cell.ToString());return result.ToArray();
    }
}
internal sealed class FrameMetrics : IDisposable
{
    private Process? process;
    private int pid;
    private readonly object gate=new();
    private FrameAccumulator frames=new();
    private string status="FPS выключен";
    private string session="";
    private DateTime nextAttempt;
    private int requestedPid;
    private bool requestedEnabled;
    private volatile bool permissionDenied;
    private Task? reader;
    private Task? errorReader;
    public string Status { get { lock(gate)return status; } }
    internal static string Extract()
    {
        const string hash="364E5D98D4D134BD54DD25C22ED2CA2F4883F8BC3ED6502BEE0C151E3436D30C";
        string folder=Path.Combine(SettingsStore.Folder,"Monitoring","PresentMon-2.3.1");Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"PresentMon.exe");
        bool Valid(){try{using var f=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(f))==hash;}catch{return false;}}
        if(Valid())return path;
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("SoundShot.PresentMon")??throw new IOException("Компонент FPS отсутствует"))using(var output=File.Create(temp))input.CopyTo(output);
            using(var check=File.OpenRead(temp))if(Convert.ToHexString(SHA256.HashData(check))!=hash)throw new IOException("Повреждён компонент FPS");
            File.Move(temp,path,true);return path;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Configure(bool enabled,int processId,long startedTicks=0)
    {
        if(enabled&&processId==pid&&process is {HasExited:false})return;
        bool retry=enabled&&requestedEnabled&&processId==requestedPid&&processId>0;
        requestedEnabled=enabled;
        if(retry&&(permissionDenied||DateTime.UtcNow<nextAttempt))return;
        permissionDenied=false;
        requestedPid=processId;nextAttempt=DateTime.UtcNow.AddSeconds(10);
        Stop();lock(gate){frames=new();status=!enabled?"FPS выключен":processId==0?"Для FPS выберите игру в списке процессов":"Запускаем измерение FPS…";}
        if(!enabled||processId<=0)return;
        try
        {
            using var target=Process.GetProcessById(processId);if(target.HasExited||startedTicks!=0&&target.StartTime.ToUniversalTime().Ticks!=startedTicks){lock(gate)status="Игра закрыта. Выберите новый процесс.";return;}string name=target.ProcessName;
            session="SoundShotFrames-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
            var start=new ProcessStartInfo(Extract()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var a in new[]{"--process_id",processId.ToString(CultureInfo.InvariantCulture),"--output_stdout","--no_console_stats","--v1_metrics","--no_track_input","--no_track_gpu","--no_track_display","--session_name",session,"--terminate_on_proc_exit"})start.ArgumentList.Add(a);
            var child=Process.Start(start)??throw new IOException("Не удалось запустить FPS");process=child;pid=processId;
            reader=Task.Run(async()=>
            {
                int ms=-1,chain=-1;try{string? line;while((line=await child.StandardOutput.ReadLineAsync())!=null){if(line.Length>8192)continue;var cells=FrameAccumulator.Csv(line);
                    if(cells.Contains("MsBetweenPresents")){ms=Array.IndexOf(cells,"MsBetweenPresents");chain=Array.IndexOf(cells,"SwapChainAddress");continue;}
                    if(ms<0||cells.Length<=ms||!double.TryParse(cells[ms],NumberStyles.Float,CultureInfo.InvariantCulture,out var duration))continue;
                    lock(gate){if(!ReferenceEquals(process,child))continue;frames.Add(DateTime.UtcNow,chain>=0&&chain<cells.Length?cells[chain]:"default",duration);status="FPS: "+name;}
                }}catch(Exception ex){lock(gate)if(ReferenceEquals(process,child))status="FPS недоступен: "+ex.Message;}
                finally{lock(gate)if(ReferenceEquals(process,child)&&!permissionDenied)status="Сбор FPS остановлен. Если игра работает, проверьте права доступа.";}
            });
            errorReader=Task.Run(async()=>{try{string? line;while((line=await child.StandardError.ReadLineAsync())!=null)if(!string.IsNullOrWhiteSpace(line))lock(gate)if(ReferenceEquals(process,child)){if(line.Contains("access denied",StringComparison.OrdinalIgnoreCase))permissionDenied=true;status=permissionDenied?"FPS: нет доступа к трассировке Windows. Используйте RTSS или запуск с разрешёнными правами; повтор — выключить и включить FPS.":"FPS: "+line[..Math.Min(line.Length,220)];}}catch{}});
        }
        catch(Exception ex){lock(gate)status="FPS недоступен: "+ex.Message;}
    }
    public (double? Fps,double? Time,double? Low) Read(){lock(gate)return frames.Read(DateTime.UtcNow);}
    private void Stop()
    {
        var child=process;process=null;pid=0;if(child==null)return;
        try
        {
            if(!child.HasExited)
            {
                // Stop only the private ETW session created by this instance.
                var stop=new ProcessStartInfo(Extract()){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                foreach(var a in new[]{"--session_name",session,"--terminate_existing_session"})stop.ArgumentList.Add(a);
                using var command=Process.Start(stop);command?.WaitForExit(1500);
                if(!child.WaitForExit(1500))child.Kill();
            }
        }catch{}finally{try{Task.WaitAll(new[]{reader??Task.CompletedTask,errorReader??Task.CompletedTask},1000);}catch{}child.Dispose();}
    }
    public void Dispose()=>Stop();
}
