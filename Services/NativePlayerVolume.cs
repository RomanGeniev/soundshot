using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace GraphiteCompanion.Services;

public sealed record PlayerVolume(double? Percent,string Message);
// Uses only the matching player's exposed volume RangeValue. Never touches Core Audio,
// the Windows mixer, keys, focus, window placement, or another tab's ambiguous slider.
public static class NativePlayerVolume
{
    private static readonly object gate=new();
    private static Task<PlayerVolume>? pending;
    private static AutomationElement? cached;
    private static AutomationElement? cachedWindow;
    private static bool cachedBrowser;
    private static int cachedProcess;
    private static string cachedKey="";
    private static readonly PlayerVolume unavailable=new(null,"Плеер не предоставляет доступ к своей громкости. Откройте его окно или нужную вкладку; системная громкость не меняется.");
    public static bool VolumeLabel(string text)=>new[]{"volume","громк","lautst","volumen","volumelevel","音量","볼륨"}.Any(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
    public static string[] ProcessNames(string source)
    {
        string s=source.ToLowerInvariant();foreach(var (id,names) in new (string,string[])[]{("applemusic",new[]{"AppleMusic"}),("itunes",new[]{"iTunes"}),("spotify",new[]{"Spotify"}),("yandexmusic",new[]{"YandexMusic"}),("yandex.music",new[]{"YandexMusic"}),("msedge",new[]{"msedge"}),("chrome",new[]{"chrome"}),("firefox",new[]{"firefox"}),("opera",new[]{"opera"}),("brave",new[]{"brave"}),("yandex",new[]{"browser"}),("vlc",new[]{"vlc"}),("aimp",new[]{"AIMP"}),("zunemusic",new[]{"Microsoft.Media.Player","Music.UI"}),("mediaplayer",new[]{"Microsoft.Media.Player","wmplayer"}),("deezer",new[]{"Deezer"}),("tidal",new[]{"TIDAL"}),("foobar",new[]{"foobar2000"}),("musicbee",new[]{"MusicBee"})})if(s.Contains(id))return names;return Array.Empty<string>();
    }
    public static Task<PlayerVolume> ReadAsync(NowPlaying source,CancellationToken token=default)=>Execute(source,null,token);
    public static Task<PlayerVolume> SetAsync(NowPlaying source,double percent,CancellationToken token=default)=>Execute(source,Math.Clamp(percent,0,100),token);
    private static async Task<PlayerVolume> Execute(NowPlaying source,double? percent,CancellationToken token)
    {
        if(source.SourceAppId.Length==0)return unavailable;Task<PlayerVolume> task;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(2));
        lock(gate){if(pending is {IsCompleted:false})return new(null,"Плеер ещё отвечает. Повторите после обновления громкости.");task=pending=Task.Run(()=>Access(source,percent,deadline.Token));}
        try{return await task.WaitAsync(deadline.Token);}catch(OperationCanceledException){return new(null,"Плеер не ответил вовремя. Громкость не подтверждена.");}catch{return unavailable;}
    }
    private static PlayerVolume Access(NowPlaying source,double? percent,CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();string key=source.SourceAppId+"\n"+source.Title;
            if(cachedKey!=key){cached=null;cachedKey=key;}
            if(cached!=null&&(!cached.Current.IsEnabled||cached.Current.IsOffscreen))cached=null;
            if(cached==null)
            {
                var names=ProcessNames(source.SourceAppId);if(names.Length==0)return unavailable;var pids=new HashSet<int>();foreach(string name in names)foreach(var p in Process.GetProcessesByName(name))using(p)pids.Add(p.Id);
                bool browser=names.Any(n=>n is "chrome" or "msedge" or "firefox" or "opera" or "brave" or "browser");var candidates=new List<(AutomationElement Slider,AutomationElement Window)>();
                foreach(var pid in pids)
                {
                    token.ThrowIfCancellationRequested();var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,pid));
                    foreach(AutomationElement window in windows)
                    {
                        token.ThrowIfCancellationRequested();if(browser&&(source.Title.Length<3||!window.Current.Name.Contains(source.Title,StringComparison.OrdinalIgnoreCase)))continue;
                        var sliders=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Slider));
                        foreach(AutomationElement slider in sliders){token.ThrowIfCancellationRequested();var info=slider.Current;if(!info.IsEnabled||info.IsOffscreen||!VolumeLabel(info.Name+" "+info.AutomationId))continue;if(slider.TryGetCurrentPattern(RangeValuePattern.Pattern,out var pattern)&&!((RangeValuePattern)pattern).Current.IsReadOnly)candidates.Add((slider,window));}
                    }
                }
                if(candidates.Count!=1)return candidates.Count>1?new(null,"В плеере несколько регуляторов громкости. Оставьте открытым нужный плеер или вкладку."):unavailable;cached=candidates[0].Slider;cachedWindow=candidates[0].Window;cachedBrowser=browser;cachedProcess=cachedWindow.Current.ProcessId;
            }
            var element=cached;var range=(RangeValuePattern)element.GetCurrentPattern(RangeValuePattern.Pattern);var current=range.Current;
            if(current.IsReadOnly||!double.IsFinite(current.Maximum-current.Minimum)||current.Maximum<=current.Minimum)return unavailable;
            if(percent.HasValue)
            {
                if(!StillBound(source,token)){cached=null;return new(null,"Регулятор плеера изменился. Дождитесь повторного определения громкости.");}
                token.ThrowIfCancellationRequested();var active=MediaService.Recent;
                if(active?.SourceAppId!=source.SourceAppId||active.Title!=source.Title)return new(null,"Текущий плеер или трек изменился. Дождитесь обновления.");
                // Some UIA providers activate their window on SetValue. Do not risk minimizing
                // an exclusive game; there is deliberately no forced foreground restoration.
                SHQueryUserNotificationState(out int state);if(state==3)return new(null,"В эксклюзивной игре этот плеер нельзя безопасно регулировать через его окно.");
                token.ThrowIfCancellationRequested();range.SetValue(current.Minimum+(current.Maximum-current.Minimum)*percent.Value/100);current=range.Current;
            }
            return new(Math.Clamp((current.Value-current.Minimum)/(current.Maximum-current.Minimum)*100,0,100),"Громкость внутри "+source.App+". Общая громкость и микшер Windows не меняются.");
        }
        catch(OperationCanceledException){throw;}
        catch{cached=null;return unavailable;}
    }
    private static bool StillBound(NowPlaying source,CancellationToken token)
    {
        if(cached==null||cachedWindow==null)return false;var info=cached.Current;var window=cachedWindow.Current;
        if(info.ControlType!=ControlType.Slider||!info.IsEnabled||info.IsOffscreen||!VolumeLabel(info.Name+" "+info.AutomationId)||window.ProcessId!=cachedProcess)return false;
        using var process=Process.GetProcessById(cachedProcess);if(!ProcessNames(source.SourceAppId).Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase))return false;
        if(cachedBrowser&&(source.Title.Length<3||!window.Name.Contains(source.Title,StringComparison.OrdinalIgnoreCase)))return false;
        var id=cachedWindow.GetRuntimeId();AutomationElement? node=cached;
        for(int depth=0;depth<64&&node!=null;depth++){token.ThrowIfCancellationRequested();if(node.GetRuntimeId().SequenceEqual(id))return true;node=TreeWalker.RawViewWalker.GetParent(node);}return false;
    }
    [DllImport("shell32.dll")]private static extern int SHQueryUserNotificationState(out int state);
}
