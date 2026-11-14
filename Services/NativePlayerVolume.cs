using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace GraphiteCompanion.Services;

public sealed record PlayerVolume(double? Percent,string Message,string Diagnostic="");
// Uses only the matching player's exposed volume RangeValue. Never touches Core Audio,
// the Windows mixer, keys, focus, window placement, or another tab's ambiguous slider.
public static class NativePlayerVolume
{
    private static readonly object gate=new();
    private static Task<PlayerVolume>? pending;
    private static volatile string phase="";
    private static AutomationElement? cached;
    private static AutomationElement? cachedWindow;
    private static bool cachedBrowser;
    private static bool cachedApple;
    private static int cachedProcess;
    private static string cachedKey="";
    private static readonly PlayerVolume unavailable=new(null,"Плеер не предоставляет доступ к своей громкости. Откройте его окно или нужную вкладку; системная громкость не меняется.");
    public static bool VolumeLabel(string text)=>new[]{"volume","громк","lautst","volumen","volumelevel","音量","볼륨"}.Any(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
    public static string[] ProcessNames(string source)
    {
        if((source.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&source.Contains("music",StringComparison.OrdinalIgnoreCase))||
            (source.Contains("яндекс",StringComparison.OrdinalIgnoreCase)&&source.Contains("музык",StringComparison.OrdinalIgnoreCase)))
            return new[]{"Яндекс Музыка","YandexMusic","Yandex Music"};
        string s=source.ToLowerInvariant();foreach(var (id,names) in new (string,string[])[]{("applemusic",new[]{"AppleMusic"}),("itunes",new[]{"iTunes"}),("spotify",new[]{"Spotify"}),("yandexmusic",new[]{"YandexMusic"}),("yandex.music",new[]{"YandexMusic"}),("msedge",new[]{"msedge"}),("chrome",new[]{"chrome"}),("firefox",new[]{"firefox"}),("opera",new[]{"opera"}),("brave",new[]{"brave"}),("yandex",new[]{"browser"}),("vlc",new[]{"vlc"}),("aimp",new[]{"AIMP"}),("zunemusic",new[]{"Microsoft.Media.Player","Music.UI"}),("mediaplayer",new[]{"Microsoft.Media.Player","wmplayer"}),("deezer",new[]{"Deezer"}),("tidal",new[]{"TIDAL"}),("foobar",new[]{"foobar2000"}),("musicbee",new[]{"MusicBee"})})if(s.Contains(id))return names;return Array.Empty<string>();
    }
    public static Task<PlayerVolume> ReadAsync(NowPlaying source,CancellationToken token=default)=>Execute(source,null,token);
    public static Task<PlayerVolume> PrepareAsync(NowPlaying source,CancellationToken token=default)=>Execute(source,null,token,true);
    public static Task<PlayerVolume> SetAsync(NowPlaying source,double percent,CancellationToken token=default)=>Execute(source,Math.Clamp(percent,0,100),token);
    private static async Task<PlayerVolume> Execute(NowPlaying source,double? percent,CancellationToken token,bool reveal=false)
    {
        if(source.SourceAppId.Length==0||percent.HasValue&&!double.IsFinite(percent.Value))return unavailable;Task<PlayerVolume> task;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(4));
        lock(gate){if(pending is {IsCompleted:false})return new(null,"Плеер ещё отвечает. Повторите после обновления громкости.");phase="start";task=pending=Task.Run(()=>Access(source,percent,deadline.Token,reveal));}
        try{return await task.WaitAsync(deadline.Token);}catch(OperationCanceledException){return new(null,"Плеер не ответил вовремя. Громкость не подтверждена.",phase);}catch{return unavailable;}
    }
    private static PlayerVolume Access(NowPlaying source,double? percent,CancellationToken token,bool reveal=false)
    {
        try
        {
            token.ThrowIfCancellationRequested();var names=ProcessNames(source.SourceAppId);
            if(names.Contains("Яндекс Музыка")||names.Contains("Spotify"))return AccessNativeWebPlayer(source,names,percent,token);
            bool browser=names.Any(n=>n is "chrome" or "msedge" or "firefox" or "opera" or "brave" or "browser");string key=source.SourceAppId+(browser?"\n"+source.Title:"");
            if(cachedKey!=key){cached=null;cachedKey=key;}
            if(cached!=null&&(!cached.Current.IsEnabled||cached.Current.IsOffscreen&&!cachedApple))cached=null;
            if(cached==null)
            {
                phase="process-windows";if(names.Length==0)return unavailable;var pids=new HashSet<int>();foreach(string name in names)foreach(var p in Process.GetProcessesByName(name))using(p)pids.Add(p.Id);
                if(pids.Count==0)return new(null,"Не найдено окно приложения «"+source.App+"». Откройте плеер и повторите.");
                var candidates=new List<(AutomationElement Slider,AutomationElement Window)>();int windowCount=0,sliderCount=0,volumeCount=0;var volumeButtons=new List<AutomationElement>();
                foreach(var pid in pids){
                    token.ThrowIfCancellationRequested();
                    var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,pid));
                    foreach(AutomationElement window in windows)
                    {
                        windowCount++;
                        token.ThrowIfCancellationRequested();if(browser&&(source.Title.Length<3||!window.Current.Name.Contains(source.Title,StringComparison.OrdinalIgnoreCase)))continue;
                        phase="find-sliders";var sliders=new List<AutomationElement>();
                        foreach(AutomationElement slider in window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Slider)))sliders.Add(slider);
                        phase="read-sliders";
                        if(sliders.Count==0)foreach(AutomationElement slider in window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.IsRangeValuePatternAvailableProperty,true)))sliders.Add(slider);
                        foreach(AutomationElement slider in sliders){sliderCount++;token.ThrowIfCancellationRequested();var info=slider.Current;if(!VolumeLabel(info.Name+" "+info.AutomationId))continue;volumeCount++;if(!info.IsEnabled||info.IsOffscreen)continue;if(slider.TryGetCurrentPattern(RangeValuePattern.Pattern,out var pattern)&&!((RangeValuePattern)pattern).Current.IsReadOnly)candidates.Add((slider,window));}
                        if(names.Contains("AppleMusic"))foreach(AutomationElement button in window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)))if(button.Current.IsEnabled&&!button.Current.IsOffscreen&&VolumeLabel(button.Current.Name+" "+button.Current.AutomationId)&&button.TryGetCurrentPattern(InvokePattern.Pattern,out _))volumeButtons.Add(button);
                    }
                }
                if(candidates.Count==0&&reveal&&volumeButtons.Count==1){
                    SHQueryUserNotificationState(out int notification);if(notification==3)return new(null,"Сначала откройте громкость Apple Music вне эксклюзивной игры.");
                    token.ThrowIfCancellationRequested();((InvokePattern)volumeButtons[0].GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    PlayerVolume result=unavailable;for(int attempt=0;attempt<3;attempt++){if(token.WaitHandle.WaitOne(80))token.ThrowIfCancellationRequested();result=Access(source,percent,token);if(result.Percent.HasValue)return result;}return result;
                }
                if(candidates.Count!=1)return candidates.Count>1?new(null,"В плеере несколько регуляторов громкости. Оставьте открытым нужный плеер или вкладку."):unavailable with{Message=names.Contains("AppleMusic")?"Откройте окно Apple Music и нажмите значок звука в баннере SoundShot ещё раз.":unavailable.Message,Diagnostic=$"windows={windowCount}; sliders={sliderCount}; volume={volumeCount}; writable={candidates.Count}; volumeButtons={volumeButtons.Count}"};cached=candidates[0].Slider;cachedWindow=candidates[0].Window;cachedBrowser=browser;cachedApple=names.Contains("AppleMusic");cachedProcess=cachedWindow.Current.ProcessId;
            }
            var element=cached;var range=(RangeValuePattern)element.GetCurrentPattern(RangeValuePattern.Pattern);var current=range.Current;
            if(current.IsReadOnly||!double.IsFinite(current.Value)||!double.IsFinite(current.Maximum-current.Minimum)||current.Maximum<=current.Minimum)return unavailable;
            if(percent.HasValue)
            {
                if(!StillBound(source,token)){cached=null;return new(null,"Регулятор плеера изменился. Дождитесь повторного определения громкости.");}
                token.ThrowIfCancellationRequested();var active=MediaService.Recent;
                if(active?.SourceAppId!=source.SourceAppId||active.Title!=source.Title)return new(null,"Текущий плеер или трек изменился. Дождитесь обновления.");
                // Some UIA providers activate their window on SetValue. Do not risk minimizing
                // an exclusive game; there is deliberately no forced foreground restoration.
                SHQueryUserNotificationState(out int state);if(state==3)return new(null,"В эксклюзивной игре этот плеер нельзя безопасно регулировать через его окно.");
                token.ThrowIfCancellationRequested();range.SetValue(current.Minimum+(current.Maximum-current.Minimum)*percent.Value/100);current=range.Current;
                double Actual()=>Math.Clamp((current.Value-current.Minimum)/(current.Maximum-current.Minimum)*100,0,100);
                for(int attempt=0;attempt<4&&Math.Abs(Actual()-percent.Value)>.51;attempt++){if(token.WaitHandle.WaitOne(75))token.ThrowIfCancellationRequested();current=range.Current;}
                if(!double.IsFinite(Actual())||Math.Abs(Actual()-percent.Value)>.51)return new(double.IsFinite(Actual())?Actual():null,"Плеер не подтвердил новое значение. Показана его текущая громкость.","write-not-confirmed");
            }
            return new(Math.Clamp((current.Value-current.Minimum)/(current.Maximum-current.Minimum)*100,0,100),"Громкость внутри "+source.App+". Общая громкость и микшер Windows не меняются.");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){cached=null;return unavailable with{Diagnostic=ex.GetType().Name+" 0x"+ex.HResult.ToString("X8")};}
    }
    private static bool StillBound(NowPlaying source,CancellationToken token)
    {
        if(cached==null||cachedWindow==null)return false;var info=cached.Current;var window=cachedWindow.Current;
        if(!info.IsEnabled||info.IsOffscreen&&!cachedApple||!VolumeLabel(info.Name+" "+info.AutomationId)||window.ProcessId!=cachedProcess||!cached.TryGetCurrentPattern(RangeValuePattern.Pattern,out _))return false;
        using var process=Process.GetProcessById(cachedProcess);if(!ProcessNames(source.SourceAppId).Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase))return false;
        if(cachedBrowser&&(source.Title.Length<3||!window.Name.Contains(source.Title,StringComparison.OrdinalIgnoreCase)))return false;
        var id=cachedWindow.GetRuntimeId();AutomationElement? node=cached;
        for(int depth=0;depth<64&&node!=null;depth++){token.ThrowIfCancellationRequested();if(node.GetRuntimeId().SequenceEqual(id))return true;node=TreeWalker.RawViewWalker.GetParent(node);}return false;
    }
    private static PlayerVolume AccessNativeWebPlayer(NowPlaying source,string[] names,double? percent,CancellationToken token)
    {
        phase="native-window";
        var handles=new HashSet<IntPtr>();foreach(string name in names)foreach(var p in Process.GetProcessesByName(name))using(p){var handle=p.MainWindowHandle;if(handle!=IntPtr.Zero)handles.Add(handle);}
        if(handles.Count==0)return new(null,"Откройте окно «"+source.App+"», затем регулятор громкости в SoundShot.");
        using var automation=new NativeVolumeAutomation();
        var windows=new List<NativeVolumeAutomation.Element>();var sliders=new List<NativeVolumeAutomation.Element>();
        var candidates=new List<(NativeVolumeAutomation.Element Slider,NativeVolumeAutomation.Element Window)>();
        try{
            foreach(var handle in handles){
                token.ThrowIfCancellationRequested();var window=automation.FromHandle(handle);windows.Add(window);phase="native-find-sliders";
                var found=automation.FindSliders(window,token);sliders.AddRange(found);
                foreach(var slider in found){token.ThrowIfCancellationRequested();if(!slider.IsEnabled||!VolumeLabel(slider.Name+" "+slider.AutomationId))continue;if(slider.TryGetRange(out var candidate)){using(candidate){if(!candidate!.Read().IsReadOnly)candidates.Add((slider,window));}}}
            }
            if(candidates.Count!=1)return new(null,candidates.Count>1?"Найдено несколько регуляторов. Оставьте открытым нужное окно плеера.":"Плеер не открыл доступ к громкости. Откройте его окно и повторите.",$"native-sliders={sliders.Count}; writable={candidates.Count}");
            var selected=candidates[0];if(!selected.Slider.TryGetRange(out var range))return unavailable;
            using(range){
                var current=range!.Read();double span=current.Maximum-current.Minimum;
                if(current.IsReadOnly||!double.IsFinite(span)||span<=0||!double.IsFinite(current.Value))return unavailable;
                double Actual()=>Math.Clamp((current.Value-current.Minimum)/(current.Maximum-current.Minimum)*100,0,100);
                if(percent.HasValue){
                    phase="native-write";token.ThrowIfCancellationRequested();var active=MediaService.Recent;
                    if(active?.SourceAppId!=source.SourceAppId||active.Title!=source.Title)return new(null,"Текущий плеер или трек изменился. Дождитесь обновления.");
                    using var process=Process.GetProcessById(selected.Window.ProcessId);
                    if(!names.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase)||!automation.IsDescendantOf(selected.Slider,selected.Window,token:token))return unavailable;
                    SHQueryUserNotificationState(out int notification);if(notification==3)return new(null,"В эксклюзивной игре этот плеер нельзя безопасно регулировать через его окно.");
                    token.ThrowIfCancellationRequested();active=MediaService.Recent;
                    if(active?.SourceAppId!=source.SourceAppId||active.Title!=source.Title)return new(null,"Текущий плеер или трек изменился. Дождитесь обновления.");
                    range.SetValue(current.Minimum+span*percent.Value/100);
                    double tolerance=Math.Max(.51,Math.Min(2,current.SmallChange/span*50));
                    current=range.Read();for(int attempt=0;attempt<4&&Math.Abs(Actual()-percent.Value)>tolerance;attempt++){if(token.WaitHandle.WaitOne(75))token.ThrowIfCancellationRequested();current=range.Read();}
                    if(!double.IsFinite(Actual())||Math.Abs(Actual()-percent.Value)>tolerance)return new(double.IsFinite(Actual())?Actual():null,"Плеер не подтвердил новое значение. Показана его текущая громкость.","write-not-confirmed");
                }
                return new(Actual(),"Громкость внутри "+source.App+". Общая громкость и микшер Windows не меняются.");
            }
        }finally{foreach(var slider in sliders)slider.Dispose();foreach(var window in windows)window.Dispose();}
    }
    [DllImport("shell32.dll")]private static extern int SHQueryUserNotificationState(out int state);
}
