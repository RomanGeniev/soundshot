using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class MonitoringPanel : UserControl
{
    private MonitoringSettings options=new();
    private readonly Action<MonitoringSettings> changed;
    private readonly Action<int,long> selectProcess;
    private bool loading;
    private readonly CheckBox show=new(){Content="Показывать метрики на экране",Margin=new Thickness(0,6,12,8)};
    private readonly CheckBox fps=new(){Content="Измерять FPS игры",Margin=new Thickness(0,6,12,8)};
    private readonly ComboBox profiles=new(){MinWidth=150,DisplayMemberPath="Name",Margin=new Thickness(0,0,8,8)};
    private readonly ComboBox interval=new(){MinWidth=110,DisplayMemberPath="Name",Margin=new Thickness(0,0,8,8)};
    private readonly ComboBox backend=new(){MinWidth=180,Margin=new Thickness(0,0,8,8)};
    private readonly ComboBox processes=new(){MinWidth=240,MaxWidth=480,DisplayMemberPath="Name",Margin=new Thickness(0,0,8,8)};
    private readonly TextBlock status=MonitorUi.Text("Открываем датчики…",12),hint=MonitorUi.Text("",12),selection=MonitorUi.Text("Выберите показатель для графика за последние 2 минуты.",12);
    private readonly TextBox search=new(){MinHeight=30,Margin=new Thickness(0,0,8,8),MinWidth=220};
    private readonly DataGrid table=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single,HeadersVisibility=DataGridHeadersVisibility.Column,EnableRowVirtualization=true,EnableColumnVirtualization=true,MinHeight=180};
    private readonly ObservableCollection<MetricRow> rows=new();
    private readonly Dictionary<string,MetricRow> byId=new();
    private readonly MetricPlot plot=new(){Height=70};
    private readonly Button log=new(){Content="Начать журнал…",Margin=new Thickness(0,0,8,8),Padding=new Thickness(12,8,12,8)};
    private readonly ComboBox logLimit=new(){MinWidth=100,Margin=new Thickness(0,0,8,8)};
    private readonly TextBlock logStatus=MonitorUi.Text("",11);
    private readonly TextBlock health=MonitorUi.Text("Подключаем датчики…",12);
    private readonly Button recording=MonitorUi.Button("● Журнал записывается · остановить",()=>{});
    private readonly ComboBox group=new(){MinWidth=150,MaxWidth=260,Margin=new Thickness(0,0,8,8)};
    private readonly Dictionary<string,TextBlock> headline=new(),selectedValues=new();
    private readonly StackPanel selectedRows=new();
    private readonly StackPanel chart=new(){Margin=new Thickness(0,10,0,0),Visibility=Visibility.Collapsed};
    public event Action? LogRequested;
    private MetricSnapshot? snapshot;
    private MetricHistory history=new();
    public MonitoringPanel(Action<MonitoringSettings> change,Action edit,Action<int,long> process,Action retry)
    {
        changed=change;selectProcess=process;
        var root=new DockPanel();Content=root;
        var controls=new StackPanel();DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);
        show.Content="Метрики на экране";
        var top=new WrapPanel();top.Children.Add(show);top.Children.Add(profiles);top.Children.Add(MonitorUi.Button("Настроить оверлей…",edit,true));controls.Children.Add(top);
        recording.Visibility=Visibility.Collapsed;recording.HorizontalAlignment=HorizontalAlignment.Left;recording.Click+=(_,_)=>LogRequested?.Invoke();controls.Children.Add(recording);controls.Children.Add(health);
        var tabs=new TabControl{Margin=new Thickness(0,6,0,0)};root.Children.Add(tabs);
        var overview=new StackPanel{Margin=new Thickness(12)};
        var summary=new UniformGrid{Columns=4};
        foreach(var (id,label) in new[]{("system/cpu","CPU · загрузка"),("system/gpu/load","GPU · загрузка"),("system/memory/used","RAM · занято"),("frames/fps","FPS")}){
            var body=new StackPanel();body.Children.Add(MonitorUi.Text(label,12));var value=MonitorUi.Text("—",24);value.FontWeight=FontWeights.SemiBold;headline[id]=value;body.Children.Add(value);
            var card=MonitorUi.Card(body);card.Padding=new Thickness(12);card.Margin=new Thickness(0,0,8,12);summary.Children.Add(card);
        }
        SizeChanged+=(_,_)=>summary.Columns=ActualWidth<660?2:4;
        overview.Children.Add(summary);overview.Children.Add(MonitorUi.Text("НА ЭКРАНЕ · ВЫБРАННЫЙ ПРОФИЛЬ",11));overview.Children.Add(selectedRows);
        overview.Children.Add(MonitorUi.Text("Состав и вид строк — в «Настроить оверлей». Все датчики доступны на соседней вкладке.",12));
        tabs.Items.Add(new TabItem{Header="Обзор",Content=new ScrollViewer{Content=overview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var sensors=new DockPanel{Margin=new Thickness(12)};
        var filter=new WrapPanel();search.MinWidth=180;filter.Children.Add(search);filter.Children.Add(group);DockPanel.SetDock(filter,Dock.Top);sensors.Children.Add(filter);
        chart.Children.Add(selection);chart.Children.Add(plot);DockPanel.SetDock(chart,Dock.Bottom);sensors.Children.Add(chart);sensors.Children.Add(table);
        tabs.Items.Add(new TabItem{Header="Датчики",Content=sensors});
        var settings=new StackPanel{Margin=new Thickness(12)};
        tabs.Items.Add(new TabItem{Header="Настройки",Content=new ScrollViewer{Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        backend.ItemsSource=new[]{"Панель SoundShot","В игре через RTSS"};backend.SelectionChanged+=(_,_)=>Change(s=>s.Renderer=backend.SelectedIndex==1?"RTSS":"Window");
        interval.ItemsSource=new[]{new Rate(250,"0,25 с"),new Rate(500,"0,5 с"),new Rate(1000,"1 с"),new Rate(2000,"2 с"),new Rate(5000,"5 с")};
        interval.SelectionChanged+=(_,_)=>{if(interval.SelectedItem is Rate r)Change(s=>s.IntervalMs=r.Ms);};
        settings.Children.Add(MonitorUi.Text("СБОР И ВЫВОД",11));
        var timing=new WrapPanel();timing.Children.Add(MonitorUi.Text("Вывод  ",12));timing.Children.Add(backend);timing.Children.Add(MonitorUi.Text("Обновление  ",12));timing.Children.Add(interval);settings.Children.Add(timing);settings.Children.Add(hint);
        settings.Children.Add(fps);var frames=new WrapPanel();processes.MinWidth=180;frames.Children.Add(processes);frames.Children.Add(MonitorUi.Button("Обновить список игр",RefreshProcesses));settings.Children.Add(frames);
        show.Click+=(_,_)=>Change(s=>s.ShowOverlay=show.IsChecked==true);fps.Click+=(_,_)=>Change(s=>s.FramesEnabled=fps.IsChecked==true);
        profiles.SelectionChanged+=(_,_)=>{if(profiles.SelectedItem is MonitorProfile p)Change(s=>s.ActiveProfile=p.Id);};
        processes.SelectionChanged+=(_,_)=>{if(!loading&&processes.SelectedItem is Game g)selectProcess(g.Pid,g.Start);};
        log.SetResourceReference(StyleProperty,"GhostButton");log.Click+=(_,_)=>LogRequested?.Invoke();logLimit.ItemsSource=new[]{100,250,500,1000};logLimit.SelectionChanged+=(_,_)=>{if(logLimit.SelectedItem is int mb)Change(s=>s.LogLimitMb=mb);};var logging=new WrapPanel();logging.Children.Add(log);logging.Children.Add(MonitorUi.Text("Лимит, МБ  ",12));logging.Children.Add(logLimit);
        var diagnostics=new StackPanel();diagnostics.Children.Add(logging);diagnostics.Children.Add(logStatus);diagnostics.Children.Add(status);var actions=new WrapPanel();actions.Children.Add(MonitorUi.Button("Перечитать датчики",retry));actions.Children.Add(MonitorUi.Button("Экспорт CSV…",Export));diagnostics.Children.Add(actions);
        settings.Children.Add(new Expander{Header="Журнал и диагностика",Content=diagnostics,Margin=new Thickness(0,16,0,12)});
        settings.Children.Add(MonitorUi.Button("Справка о датчиках и RTSS",()=>MonitoringHelp.Show(Window.GetWindow(this))));
        search.ToolTip="Поиск по устройству или названию датчика";System.Windows.Automation.AutomationProperties.SetName(search,"Поиск показателей");
        group.Items.Add("Все устройства");group.SelectedIndex=0;System.Windows.Automation.AutomationProperties.SetName(group,"Устройство для просмотра датчиков");
        table.GridLinesVisibility=DataGridGridLinesVisibility.None;table.RowHeight=32;table.BorderThickness=new Thickness(0);table.MinHeight=100;
        table.SetResourceReference(BackgroundProperty,"CardBrush");table.SetResourceReference(ForegroundProperty,"TextBrush");table.SetResourceReference(BorderBrushProperty,"CardBorderBrush");
        foreach(var (title,path,width) in new[]{("Устройство","Group",1.3),("Показатель","Name",1.5),("Сейчас","Current",.8),("Мин.","Min",.8),("Макс.","Max",.8)})table.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path),Width=new DataGridLength(width,DataGridLengthUnitType.Star)});
        table.ItemsSource=rows;var view=CollectionViewSource.GetDefaultView(rows);view.Filter=o=>o is MetricRow r&&(r.Group+" "+r.Name).Contains(search.Text,StringComparison.OrdinalIgnoreCase)&&(group.SelectedIndex<=0||r.Group==group.SelectedItem as string);search.TextChanged+=(_,_)=>view.Refresh();group.SelectionChanged+=(_,_)=>view.Refresh();
        table.SelectionChanged+=(_,_)=>UpdatePlot();
        foreach(var (control,name) in new (DependencyObject,string)[]{(profiles,"Профиль экранной панели"),(interval,"Интервал обновления"),(backend,"Способ вывода метрик"),(processes,"Игра для измерения FPS")})System.Windows.Automation.AutomationProperties.SetName(control,name);
        Loaded+=(_,_)=>{if(processes.ItemsSource==null)RefreshProcesses();};
    }
    private void Change(Action<MonitoringSettings> edit){if(loading)return;var copy=options.Copy();edit(copy);changed(copy);}
    public void Apply(MonitoringSettings value)
    {
        loading=true;options=value.Normalize();show.IsChecked=options.ShowOverlay;fps.IsChecked=options.FramesEnabled;profiles.ItemsSource=options.Profiles;profiles.SelectedItem=options.Current;
        interval.SelectedItem=interval.Items.Cast<Rate>().FirstOrDefault(r=>r.Ms==options.IntervalMs);logLimit.SelectedItem=options.LogLimitMb;backend.SelectedIndex=options.Renderer=="RTSS"?1:0;processes.IsEnabled=options.FramesEnabled;loading=false;
        hint.Text=options.Renderer=="RTSS"?"Для полноэкранных игр запустите RTSS и разрешите экранную статистику для игры. Размер и положение игрового OSD задаются в RTSS; состав и цвета — здесь. Совместимость зависит от игры.":"Панель SoundShot: рабочий стол, оконные и безрамочные игры. Настройка размера, положения и графиков — в «Настроить оверлей».";
        hint.ToolTip="Недоступный датчик показан прочерком. Температуры, мощность и вентиляторы зависят от оборудования, драйверов и прав доступа. SoundShot ничего не разгоняет и не меняет параметры оборудования. FPS измеряется только для выбранного процесса; 1% low — по последним 10 секундам. История ограничена двумя минутами.";
        selectedRows.Children.Clear();selectedValues.Clear();
        foreach(var tile in options.Current.Tiles){var line=new Grid{Margin=new Thickness(0,0,0,2)};line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});line.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var label=MonitorUi.Text(tile.Label.Length>0?tile.Label:snapshot?.Values.FirstOrDefault(v=>v.Id==tile.MetricId)?.Name??tile.MetricId);var reading=MonitorUi.Text("—");reading.FontWeight=FontWeights.SemiBold;Grid.SetColumn(reading,1);line.Children.Add(label);line.Children.Add(reading);selectedRows.Children.Add(line);selectedValues[tile.MetricId+"/"+selectedRows.Children.Count]=reading;}
        if(options.Current.Tiles.Count==0)selectedRows.Children.Add(MonitorUi.Text("В профиле пока нет показателей. Добавьте их в настройках оверлея."));
        UpdateSummary();
    }
    public void SetMessage(string message){status.Text=message;health.Text=message;}
    public void SetLogState(bool running,string message){log.Content=running?"Остановить журнал":"Начать журнал…";recording.Visibility=running?Visibility.Visible:Visibility.Collapsed;logLimit.IsEnabled=!running;logStatus.Text=message;logStatus.Visibility=message.Length>0?Visibility.Visible:Visibility.Collapsed;if(!running&&message.Length>0)health.Text=message;}
    public void Update(MetricSnapshot? value,MetricHistory samples,string? outputStatus=null)
    {
        snapshot=value;history=samples;bool stale=value==null||DateTime.UtcNow-value.Timestamp>TimeSpan.FromSeconds(10);
        var incoming=(value?.Values??new()).Take(4096).GroupBy(v=>v.Id).Select(g=>g.Last()).ToDictionary(v=>v.Id);
        int newRows=incoming.Keys.Count(id=>!byId.ContainsKey(id));
        foreach(var missing in byId.Keys.Where(id=>!incoming.ContainsKey(id)).ToArray()){if(byId.Count+newRows<=4096)break;rows.Remove(byId[missing]);byId.Remove(missing);}
        foreach(var pair in incoming){if(!byId.TryGetValue(pair.Key,out var row)){row=new(pair.Value);byId[pair.Key]=row;rows.Add(row);}row.Update(pair.Value,stale);}
        foreach(var pair in byId)if(!incoming.ContainsKey(pair.Key))pair.Value.Update(null,true);
        foreach(var name in incoming.Values.Select(v=>v.Group).Distinct().OrderBy(v=>v))if(!group.Items.Contains(name))group.Items.Add(name);
        status.Text=(stale?"Нет свежих данных. ":"")+(value?.Status??"Открываем датчики…")+(string.IsNullOrEmpty(outputStatus)?"":" · "+outputStatus);
        health.Text=stale?"Нет свежих данных — проверьте диагностику.":!string.IsNullOrEmpty(outputStatus)?outputStatus:$"Датчиков доступно: {incoming.Values.Count(v=>v.Value.HasValue)} из {incoming.Count}"+(options.FramesEnabled?" · FPS включён":"");
        UpdateSummary();
        UpdatePlot();
    }
    private void UpdateSummary()
    {
        bool stale=snapshot==null||DateTime.UtcNow-snapshot.Timestamp>TimeSpan.FromSeconds(10);
        var values=snapshot?.Values.GroupBy(v=>v.Id).ToDictionary(g=>g.Key,g=>g.Last())??new();
        foreach(var (id,text) in headline){values.TryGetValue(id,out var metric);text.Text=id=="frames/fps"&&!options.FramesEnabled?"Выключен":MetricText.Format(stale?null:metric?.Value,metric?.Unit??"");}
        int i=0;foreach(var tile in options.Current.Tiles){i++;if(!selectedValues.TryGetValue(tile.MetricId+"/"+i,out var text))continue;values.TryGetValue(tile.MetricId,out var metric);text.Text=MetricText.Format(stale?null:metric?.Value,metric?.Unit??"");}
    }
    private void UpdatePlot()
    {
        if(table.SelectedItem is not MetricRow row){chart.Visibility=Visibility.Collapsed;plot.SetValues(Array.Empty<double?>());return;}chart.Visibility=Visibility.Visible;
        selection.Text=row.Group+" · "+row.Name+" · последние 2 минуты";plot.Minimum=row.Unit=="%"?0:null;plot.Maximum=row.Unit=="%"?100:null;
        plot.SetValues(snapshot==null||DateTime.UtcNow-snapshot.Timestamp>TimeSpan.FromSeconds(10)?Array.Empty<double?>():history.Get(row.Id));
    }
    private void RefreshProcesses()
    {
        var old=processes.SelectedItem as Game;var games=new List<Game>{new(0,0,"Выберите игру…")};
        foreach(var p in Process.GetProcesses())using(p){try{if(p.Id!=Environment.ProcessId&&p.MainWindowHandle!=IntPtr.Zero)games.Add(new(p.Id,p.StartTime.ToUniversalTime().Ticks,$"{p.ProcessName} · {p.Id}"));}catch{}}
        loading=true;processes.ItemsSource=games.OrderBy(g=>g.Pid==0?"":g.Name).ToArray();processes.SelectedItem=games.FirstOrDefault(g=>g.Pid==old?.Pid&&g.Start==old?.Start)??games[0];loading=false;
        if(processes.SelectedItem is Game selected&&(old?.Pid!=selected.Pid||old?.Start!=selected.Start))selectProcess(selected.Pid,selected.Start);
    }
    private void Export()
    {
        if(snapshot==null){status.Text="Пока нет данных для экспорта.";return;}
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="SoundShot-метрики-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{string Q(string s)=>"\""+s.Replace("\"","\"\"")+"\"";var lines=new List<string>{"Время;Устройство;Показатель;Единица;Значение;Минимум;Максимум"};lines.AddRange(snapshot.Values.Select(v=>string.Join(";",new[]{snapshot.Timestamp.ToLocalTime().ToString("O"),v.Group,v.Name,v.Unit,v.Value?.ToString(CultureInfo.InvariantCulture)??"",v.Minimum?.ToString(CultureInfo.InvariantCulture)??"",v.Maximum?.ToString(CultureInfo.InvariantCulture)??""}.Select(Q))));File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true));status.Text="Текущие значения сохранены в CSV.";}catch(Exception ex){status.Text="Не удалось сохранить CSV: "+ex.Message;}
    }
    private sealed record Rate(int Ms,string Name);
    private sealed record Game(int Pid,long Start,string Name);
    private sealed class MetricRow : INotifyPropertyChanged
    {
        public string Id {get;}public string Group {get;private set;}public string Name {get;private set;}public string Unit {get;private set;}public string Current {get;private set;}="—";public string Min {get;private set;}="—";public string Max {get;private set;}="—";
        public MetricRow(MetricValue v){Id=v.Id;Group=v.Group;Name=v.Name;Unit=v.Unit;}
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(MetricValue? v,bool stale){if(v!=null){Group=v.Group;Name=v.Name;Unit=v.Unit;}Current=MetricText.Format(stale?null:v?.Value,Unit);Min=MetricText.Format(v?.Minimum,Unit);Max=MetricText.Format(v?.Maximum,Unit);PropertyChanged?.Invoke(this,new(null));}
    }
}


