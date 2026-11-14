using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GraphiteCompanion.Services;

// RTSS SDK shared-memory v2.14+ protocol. No injection, process launch, or global profile edits.
// Open the existing server mapping and own one secondary OSD slot; never overwrite other clients.
public sealed class RtssOutput : IDisposable
{
    private readonly string owner="SoundShot-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N")[..12];
    private readonly string mapping;
    private bool published;
    private readonly object gate=new();
    private Timer? releaseRetry;
    public RtssOutput(string mappingName="RTSSSharedMemoryV2")=>mapping=mappingName;
    public static bool ReadFrames(int processId,long startedTicks,out double? fps,out double? milliseconds,string mappingName="RTSSSharedMemoryV2")
    {
        fps=milliseconds=null;if(processId<=0)return false;
        try
        {
            if(startedTicks!=0){using var process=System.Diagnostics.Process.GetProcessById(processId);if(process.StartTime.ToUniversalTime().Ticks!=startedTicks)return false;}
            using var file=MemoryMappedFile.OpenExisting(mappingName,MemoryMappedFileRights.Read);using var view=file.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
            if(view.Capacity<36||view.ReadUInt32(0)!=0x52545353||view.ReadUInt32(4)<0x20000||view.ReadUInt32(4)>=0x30000)return false;
            long size=view.ReadUInt32(8),offset=view.ReadUInt32(12),count=view.ReadUInt32(16);if(size<284||size>1048576||offset<36||count>4096||offset+size*count>view.Capacity)return false;
            for(int i=0;i<count;i++){long entry=offset+i*size;if(view.ReadUInt32(entry)!=processId)continue;uint end=view.ReadUInt32(entry+272),begin=view.ReadUInt32(entry+268),frames=view.ReadUInt32(entry+276),time=view.ReadUInt32(entry+280);uint duration=unchecked(end-begin);
                // Skip torn or stale measurements, but keep RTSS as the source while it owns this game.
                if(end!=view.ReadUInt32(entry+272)||begin==0||duration==0||unchecked((uint)Environment.TickCount-end)>3000)return true;
                fps=1000d*frames/duration;milliseconds=time>0?time/1000d:null;return true;}
        }
        catch{}return false;
    }
    public string Publish(MonitorProfile profile,MetricSnapshot? snapshot)
    {
        lock(gate){releaseRetry?.Dispose();releaseRetry=null;
        string text=Format(profile,snapshot);
        var result=Write(text,false);if(result==null){published=true;return "Метрики переданы RTSS. Видимость в игре зависит от её профиля RTSS.";}return result;
        }
    }
    public static string Format(MonitorProfile profile,MetricSnapshot? snapshot)
    {
        var p=profile.Normalize();bool stale=snapshot==null||DateTime.UtcNow-snapshot.Timestamp>TimeSpan.FromSeconds(10);
        var values=snapshot?.Values.GroupBy(v=>v.Id).ToDictionary(g=>g.Key,g=>g.Last());var text=new StringBuilder();
        string Plain(string s)=>new string(s.Where(c=>!char.IsControl(c)).ToArray()).Replace('<','‹').Replace('>','›');
        // Emit complete rows only. The SDK's extended text field is 4096 bytes, including terminator.
        var encoding=Ansi();foreach(var tile in p.Tiles){MetricValue? v=null;values?.TryGetValue(tile.MetricId,out v);string color=(tile.Color.Length>0?tile.Color:p.Foreground)[1..];
            string line=(text.Length>0?(p.Direction=="Inline"?"   ":"\n"):"")+"<C="+color+">"+Plain(tile.Label.Length>0?tile.Label:v?.Name??tile.MetricId)+": "+Plain(MetricText.Format(stale?null:v?.Value,v?.Unit??"",p.ShowUnits))+"<C>";
            if(encoding.GetByteCount(text.ToString())+encoding.GetByteCount(line)>4000){text.Append(p.Direction=="Inline"?"   ...":"\n...");break;}text.Append(line);}
        return text.Length==0?"SoundShot: показатели не выбраны":text.ToString();
    }
    private static Encoding Ansi(){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding((int)GetACP());}
    private unsafe string? Write(string text,bool release)
    {
        try
        {
            using var file=MemoryMappedFile.OpenExisting(mapping,MemoryMappedFileRights.ReadWrite);
            using var view=file.CreateViewAccessor(0,0,MemoryMappedFileAccess.ReadWrite);
            if(view.Capacity<40||view.ReadUInt32(0)!=0x52545353||view.ReadUInt32(4)<0x0002000e||view.ReadUInt32(4)>=0x00030000)return "Версия общей памяти RTSS не поддерживается. Обновите RTSS.";
            long size=view.ReadUInt32(20),offset=view.ReadUInt32(24),count=view.ReadUInt32(28);
            if(size<4608||size>1048576||count<2||count>256||offset<40||offset+size*count>view.Capacity)return "Некорректная структура данных RTSS.";
            byte* pointer=null;view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                ref int busy=ref *(int*)(pointer+view.PointerOffset+36);
                if(Interlocked.CompareExchange(ref busy,1,0)!=0)return "RTSS занят обновлением; повторим при следующем опросе.";
                try
                {
                    long slot=-1,empty=-1;
                    for(int i=1;i<count;i++){long entry=offset+i*size;var bytes=new byte[256];view.ReadArray(entry+256,bytes,0,256);int end=Array.IndexOf(bytes,(byte)0);if(end<0)continue;string name=Encoding.ASCII.GetString(bytes,0,end);if(name==owner){slot=entry;break;}if(name.Length==0&&empty<0)empty=entry;}
                    if(slot<0){if(release)return null;slot=empty;}if(slot<0)return "В RTSS нет свободного места для панели SoundShot.";
                    if(release){view.Write(slot,(byte)0);view.Write(slot+512,(byte)0);view.Write(slot+256,(byte)0);}
                    else{var owned=Encoding.ASCII.GetBytes(owner+"\0");view.WriteArray(slot+256,owned,0,owned.Length);var bytes=Ansi().GetBytes(text+"\0");if(bytes.Length>4096)return "Слишком длинная панель для RTSS.";view.Write(slot,(byte)0);view.WriteArray(slot+512,bytes,0,bytes.Length);}
                    view.Write(32,unchecked(view.ReadUInt32(32)+1));return null;
                }
                finally{Interlocked.Exchange(ref busy,0);}
            }
            finally{view.SafeMemoryMappedViewHandle.ReleasePointer();}
        }
        catch(FileNotFoundException){return release?null:"RTSS не запущен. Установите и запустите RivaTuner Statistics Server.";}
        catch(UnauthorizedAccessException){return "Нет доступа к RTSS. SoundShot и RTSS должны работать с одинаковыми правами.";}
        catch(Exception ex){return "RTSS недоступен: "+ex.Message;}
    }
    public void Dispose()
    {
        lock(gate){if(!published)return;if(Write("",true)==null){published=false;releaseRetry?.Dispose();releaseRetry=null;return;}
            // Another client may be writing. Keep ownership until cleanup really succeeds.
            releaseRetry??=new Timer(_=>{lock(gate){if(!published||Write("",true)==null){published=false;releaseRetry?.Dispose();releaseRetry=null;}}},null,100,1000);
        }
    }
    [DllImport("kernel32.dll")]private static extern uint GetACP();
}
