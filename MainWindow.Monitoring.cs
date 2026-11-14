using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GraphiteCompanion.Controls;
using GraphiteCompanion.Services;

namespace GraphiteCompanion;
public partial class MainWindow
{
    private MonitoringService? monitoring;
    private MonitoringSettings monitorOptions=new();
    private MonitoringPanel? monitoringPanel;
    private MonitoringEditor? monitoringEditor;
    private MetricsOverlay? metricsOverlay,metricsPlacement;
    private readonly RtssOutput rtssOutput=new();
    private readonly MetricHistory metricsHistory=new();
    private MetricSnapshot? metricsSnapshot,pendingMetrics;
    private int metricsPosted,metricsProcess;
    private long metricsProcessStarted;
    private readonly DispatcherTimer metricsWatchdog=new(){Interval=TimeSpan.FromSeconds(1)};
    private string metricsOutputStatus="";
    private bool metricsStale;
    private MetricsLogWriter? metricsLog;
    private bool metricsLogBusy;
    private void InitMonitoring()
    {
        monitorOptions=(settings.Monitoring??new()).Normalize();
        monitoring=new();monitoring.Updated+=ReceiveMetrics;
        monitoringPanel=new(value=>{string? error=SaveMonitoring(value);if(error!=null)monitoringPanel?.SetMessage(error);},EditMonitoring,(pid,start)=>{metricsProcess=pid;metricsProcessStarted=start;metricsLog?.Event("processSelected",LogConfiguration());RefreshMonitoringLifetime();},async()=>{if(monitoring==null)return;metricsLog?.Event("sensorsRestart");await monitoring.SetActiveAsync(false);metricsHistory.Clear();RefreshMonitoringLifetime();});
        PageMonitoring.Content=monitoringPanel;monitoringPanel.Apply(monitorOptions);
        monitoringPanel.LogRequested+=async()=>await ToggleMetricsLog();
        IsVisibleChanged+=(_,_)=>RefreshMonitoringLifetime();StateChanged+=(_,_)=>RefreshMonitoringLifetime();
        metricsWatchdog.Tick+=(_,_)=>{bool stale=metricsSnapshot==null||DateTime.UtcNow-metricsSnapshot.Timestamp>TimeSpan.FromSeconds(10);if(stale&&!metricsStale){metricsStale=true;RenderMetrics();}};
        RefreshMetricsOutput();RefreshMonitoringLifetime();
    }
    private bool MonitoringNeeded=>!shutdownStarted&&(metricsLog is {Running:true}||monitorOptions.ShowOverlay||monitoringEditor!=null||metricsPlacement!=null||IsVisible&&WindowState!=WindowState.Minimized&&layoutPage=="Monitoring");
    private async void RefreshMonitoringLifetime()
    {
        if(monitoring==null)return;bool needed=MonitoringNeeded;
        monitoring.Configure(new(monitorOptions.IntervalMs,metricsProcess,monitorOptions.FramesEnabled,metricsProcessStarted));
        if(needed)metricsWatchdog.Start();else metricsWatchdog.Stop();
        try{await monitoring.SetActiveAsync(needed);}catch(Exception ex){SettingsStore.LogError(ex);monitoringPanel?.SetMessage("Не удалось запустить мониторинг: "+ex.Message);}
    }
    private void ReceiveMetrics(MetricSnapshot value)
    {
        metricsLog?.Record(value);
        Interlocked.Exchange(ref pendingMetrics,value);if(Interlocked.Exchange(ref metricsPosted,1)!=0)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{Interlocked.Exchange(ref metricsPosted,0);var next=Interlocked.Exchange(ref pendingMetrics,null);if(shutdownStarted||next==null)return;metricsSnapshot=next;metricsHistory.Add(next);metricsStale=false;RenderMetrics();}));
    }
    private void RenderMetrics()
    {
        if(shutdownStarted)return;
        if(monitorOptions.ShowOverlay&&monitorOptions.Renderer=="RTSS")metricsOutputStatus=rtssOutput.Publish(monitorOptions.Current,metricsSnapshot);
        if(monitoringPanel is {IsVisible:true})monitoringPanel.Update(metricsSnapshot,metricsHistory,metricsOutputStatus);
        metricsOverlay?.Update(metricsSnapshot,metricsHistory);monitoringEditor?.Update(metricsSnapshot,metricsHistory);metricsPlacement?.Update(metricsSnapshot,metricsHistory);
    }
    private string? SaveMonitoring(MonitoringSettings value)
    {
        var normalized=value.Normalize();var previous=settings.Monitoring;settings.Monitoring=normalized;
        if(!SettingsStore.TrySave(settings,out var error)){settings.Monitoring=previous;monitoringPanel?.Apply(monitorOptions);return error;}
        monitorOptions=normalized;metricsLog?.Event("configuration",LogConfiguration());monitoringPanel?.Apply(monitorOptions);RefreshMetricsOutput();RefreshMonitoringLifetime();return null;
    }
    private void ToggleMetricsOverlay()
    {
        if(monitoring==null||shutdownStarted)return;var next=monitorOptions.Copy();next.ShowOverlay=!next.ShowOverlay;var error=SaveMonitoring(next);if(error!=null)monitoringPanel?.SetMessage(error);
    }
    private void RefreshMetricsOutput()
    {
        if(shutdownStarted)return;
        metricsOutputStatus="";
        if(monitorOptions.ShowOverlay&&monitorOptions.Renderer=="Window"&&metricsPlacement==null)
        {
            if(metricsOverlay==null){metricsOverlay=new();metricsOverlay.Closed+=(_,_)=>metricsOverlay=null;}
            metricsOverlay.Apply(monitorOptions.Current);
        }
        else{metricsOverlay?.Close();metricsOverlay=null;}
        if(!monitorOptions.ShowOverlay||monitorOptions.Renderer!="RTSS")rtssOutput.Dispose();RenderMetrics();
    }
    private void EditMonitoring()
    {
        if(shutdownStarted)return;if(monitoringEditor!=null){monitoringEditor.Show();monitoringEditor.Activate();return;}
        var catalog=metricsSnapshot?.Values??new List<MetricValue>{new("system/cpu","Система","CPU","%",null),new("system/gpu/load","GPU","GPU","%",null),new("system/memory/used","Система","Оперативная память","ГБ",null),new("frames/fps","Игра","FPS","кадр/с",null),new("frames/time","Игра","Время кадра","мс",null),new("frames/low","Игра","1% low","кадр/с",null)};
        monitoringEditor=new(monitorOptions,catalog,SaveMonitoring,(profile,done)=>
        {
            metricsOverlay?.Close();metricsOverlay=null;metricsPlacement=new(true);var placement=metricsPlacement;
            placement.Closed+=(_,_)=>{metricsPlacement=null;if(!shutdownStarted)done(placement.Accepted?placement.Result:null);RefreshMetricsOutput();RefreshMonitoringLifetime();};
            placement.Apply(profile);placement.Update(metricsSnapshot,metricsHistory);RefreshMonitoringLifetime();
        }){Owner=this,ShowInTaskbar=false};
        monitoringEditor.Closed+=(_,_)=>{monitoringEditor=null;RefreshMonitoringLifetime();};monitoringEditor.Show();monitoringEditor.Update(metricsSnapshot,metricsHistory);RefreshMonitoringLifetime();
    }
    private async Task StopMonitoring()
    {
        metricsWatchdog.Stop();rtssOutput.Dispose();metricsOverlay?.Close();metricsPlacement?.Close();monitoringEditor?.Close();
        if(monitoring!=null){monitoring.Updated-=ReceiveMetrics;await monitoring.DisposeAsync();}
        if(metricsLog!=null){var flush=metricsLog.DisposeAsync().AsTask();if(await Task.WhenAny(flush,Task.Delay(3000))==flush)await flush;else SettingsStore.LogError(new TimeoutException("Закрытие журнала метрик не завершилось за 3 секунды: "+metricsLog.Folder));}
    }
    private object LogConfiguration()=>new{monitorOptions.IntervalMs,monitorOptions.FramesEnabled,processId=metricsProcess,processStartedUtcTicks=metricsProcessStarted,monitorOptions.Renderer,monitorOptions.ShowOverlay,profile=monitorOptions.Current,monitors=System.Windows.Forms.Screen.AllScreens.Select(s=>new{s.DeviceName,s.Primary,bounds=new{s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height}}).ToArray(),culture=System.Globalization.CultureInfo.CurrentCulture.Name,timeZone=TimeZoneInfo.Local.Id};
    private async Task ToggleMetricsLog()
    {
        if(metricsLogBusy||shutdownStarted)return;metricsLogBusy=true;
        try
        {
            if(metricsLog is {Running:true}){await metricsLog.DisposeAsync();monitoringPanel?.SetLogState(false,metricsLog.Status);metricsLog=null;RefreshMonitoringLifetime();return;}
            var picker=new Microsoft.Win32.OpenFolderDialog{Title="Папка для диагностического журнала метрик"};if(picker.ShowDialog(this)!=true)return;
            metricsLog=new(picker.FolderName,monitorOptions.LogLimitMb,LogConfiguration());var writer=metricsLog;
            writer.StateChanged+=message=>Dispatcher.BeginInvoke(new Action(()=>{if(!ReferenceEquals(metricsLog,writer)||shutdownStarted)return;monitoringPanel?.SetLogState(writer.Running,message);RefreshMonitoringLifetime();}));
            writer.Event("started");monitoringPanel?.SetLogState(true,"Записываем все датчики: "+writer.Folder);RefreshMonitoringLifetime();
        }
        catch(Exception ex){monitoringPanel?.SetLogState(false,"Не удалось записать журнал: "+ex.Message);}
        finally{metricsLogBusy=false;}
    }
}
