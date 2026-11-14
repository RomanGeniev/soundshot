using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Threading;
using GraphiteCompanion.Services;
using Forms=System.Windows.Forms;

namespace GraphiteCompanion.Controls;

internal static class MonitorUi
{
    public static TextBlock Text(string value,double size=13){var t=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,FontSize=size,Margin=new Thickness(0,3,0,6)};t.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");return t;}
    public static Button Button(string text,Action click,bool accent=false){var b=new Button{Content=text,Margin=new Thickness(0,0,6,6),Padding=new Thickness(12,8,12,8)};AutomationProperties.SetName(b,text);b.SetResourceReference(FrameworkElement.StyleProperty,accent?"AccentButton":"GhostButton");b.Click+=(_,_)=>click();return b;}
    public static StackPanel Row(params UIElement[] values){var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var v in values)row.Children.Add(v);return row;}
    public static Border Card(UIElement child){var b=new Border{Child=child,Padding=new Thickness(16),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,12)};b.SetResourceReference(Border.BackgroundProperty,"CardBrush");b.SetResourceReference(Border.BorderBrushProperty,"CardBorderBrush");return b;}
    public static ComboBox Combo<T>(IEnumerable<T> options,T selected,Action<T> change){var box=new ComboBox{ItemsSource=options.ToArray(),SelectedItem=selected,MinHeight=32,Margin=new Thickness(0,0,0,8)};box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is T v)change(v);};return box;}
}
public sealed class MonitoringEditor : Window
{
    private readonly MonitoringSettings draft;
    private readonly Func<MonitoringSettings,string?> save;
    private readonly Action<MonitorProfile,Action<MonitorProfile?>> place;
    private readonly List<MetricValue> catalog;
    private readonly MetricView preview=new();
    private readonly ScrollViewer previewScroller=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=280};
    private readonly ContentControl editor=new();
    private readonly Grid body=new();
    private readonly ScrollViewer previewHost=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly StackPanel previewHeader=new();
    private readonly Border previewBackdrop=new(){Background=new SolidColorBrush(Color.FromRgb(55,65,78)),Padding=new Thickness(10),CornerRadius=new CornerRadius(8)};
    private readonly ComboBox profiles=new(){MinWidth=180};
    private readonly TextBlock error=MonitorUi.Text("");
    private readonly Button apply;
    private MetricSnapshot? snapshot;
    private MetricHistory history=new();
    private readonly HashSet<object> invalid=new();
    private MonitorProfile P=>draft.Current;
    private bool building;
    private int selectedTab;
    private bool advancedOpen;
    private readonly ScrollViewer editorScroller=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly Dictionary<string,ScrollViewer> detailScrollers=new();
    private string? selectedMetric;
    private bool addOpen,metricOptionsOpen;
    private string sensorSearch="";
    private Action? refreshMetricOptions,refreshPosition;
    private readonly List<FrameworkElement> verticalControls=new();
    public bool Saved {get;private set;}
    public MonitoringSettings Draft=>draft.Copy();
    public MonitoringEditor(MonitoringSettings current,IEnumerable<MetricValue> available,Func<MonitoringSettings,string?> commit,Action<MonitorProfile,Action<MonitorProfile?>> placement)
    {
        draft=current.Normalize();save=commit;place=placement;catalog=available.GroupBy(m=>m.Id).Select(g=>g.Last()).ToList();
        Title="Метрики на экране";Width=880;Height=650;MinWidth=600;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=13;
        SourceInitialized+=(_,_)=>FitEditor();
        SetResourceReference(BackgroundProperty,"BgBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        apply=MonitorUi.Button("Сохранить",Commit,true);
        var bottom=MonitorUi.Row(apply,MonitorUi.Button("Отмена",Close));bottom.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        profiles.DisplayMemberPath="Name";profiles.SelectionChanged+=(_,_)=>{if(building||profiles.SelectedItem is not MonitorProfile p)return;draft.ActiveProfile=p.Id;BuildEditor();};
        body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(250)});body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});root.Children.Add(body);
        SizeChanged+=(_,_)=>body.ColumnDefinitions[1].Width=new GridLength(ActualWidth<800?210:250);
        editor.Margin=new Thickness(0,12,16,0);editor.Content=editorScroller;body.Children.Add(editor);
        var previewColumn=new StackPanel{Margin=new Thickness(0,12,0,0)};previewHost.Content=previewColumn;Grid.SetColumn(previewHost,1);body.Children.Add(previewHost);
        var previewTitle=MonitorUi.Text("ПРЕДПРОСМОТР",12);previewTitle.VerticalAlignment=VerticalAlignment.Center;previewTitle.Margin=new Thickness(0,0,16,8);previewHeader.Children.Add(previewTitle);
        previewHeader.Children.Add(MonitorUi.Button("Разместить на экране",()=>{if(invalid.Count>0)return;Hide();place(P.Copy(),result=>{if(result!=null){P.X=result.X;P.Y=result.Y;P.Monitor=result.Monitor;refreshPosition?.Invoke();}Show();Activate();});}));previewColumn.Children.Add(previewHeader);
        previewScroller.Content=new Viewbox{Child=preview,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top};previewBackdrop.Child=previewScroller;previewColumn.Children.Add(previewBackdrop);
        previewColumn.Children.Add(MonitorUi.Text("Изменения применятся после сохранения.",11));
        RefreshProfiles();
    }
    private void RefreshProfiles(){building=true;profiles.ItemsSource=null;profiles.ItemsSource=draft.Profiles;profiles.SelectedItem=P;building=false;BuildEditor();}
    private void Add(bool copy){if(draft.Profiles.Count>=24){error.Text="Можно сохранить до 24 профилей.";return;}var p=copy?P.Copy():new MonitorProfile();p.Id=Guid.NewGuid().ToString("N");p.Name=copy?P.Name+" · копия":"Профиль "+(draft.Profiles.Count+1);draft.Profiles.Add(p);draft.ActiveProfile=p.Id;RefreshProfiles();}
    private void Delete(){if(draft.Profiles.Count==1){error.Text="Оставьте хотя бы один профиль. Удаление можно отменить кнопкой «Отмена».";return;}draft.Profiles.Remove(P);draft.ActiveProfile=draft.Profiles[0].Id;RefreshProfiles();}
    private void Commit()
    {
        if(invalid.Count>0){error.Text="Проверьте выделенные значения.";return;}
        if(string.IsNullOrWhiteSpace(P.Name)){error.Text="Введите название профиля.";return;}
        if(draft.Profiles.Any(p=>p.Tiles.Any(t=>t.GraphMin.HasValue&&t.GraphMax.HasValue&&t.GraphMax<=t.GraphMin))){error.Text="Верхняя граница графика должна быть больше нижней.";return;}
        var message=save(draft.Normalize());if(message!=null){error.Text=message;return;}Saved=true;Close();
    }
    public void Update(MetricSnapshot? value,MetricHistory samples){snapshot=value;history=samples;preview.Update(snapshot,history);}
    private void RefreshPreview(){
        bool inline=P.Direction=="Inline";
        body.RowDefinitions[0].Height=inline?GridLength.Auto:new GridLength(1,GridUnitType.Star);body.RowDefinitions[1].Height=inline?new GridLength(1,GridUnitType.Star):GridLength.Auto;
        Grid.SetRow(editor,inline?1:0);Grid.SetColumnSpan(editor,inline?2:1);editor.Margin=new Thickness(0,12,inline?0:16,0);
        Grid.SetColumn(previewHost,inline?0:1);Grid.SetColumnSpan(previewHost,inline?2:1);previewHeader.Orientation=inline?Orientation.Horizontal:Orientation.Vertical;
        previewBackdrop.MinHeight=inline?38:90;previewBackdrop.MaxHeight=inline?110:300;previewScroller.MaxHeight=inline?90:280;
        previewScroller.HorizontalScrollBarVisibility=inline?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;preview.Apply(P);preview.Update(snapshot,history);apply.IsEnabled=invalid.Count==0;
    }
    private TextBox StringField(string value,Action<string> set,Func<string,bool>? validate=null)
    {
        var field=new TextBox{Text=value,MinHeight=30,Margin=new Thickness(0,0,0,8)};
        field.TextChanged+=(_,_)=>{bool ok=validate?.Invoke(field.Text)??true;if(ok){invalid.Remove(field);field.ClearValue(BorderBrushProperty);set(field.Text);}else{invalid.Add(field);field.BorderBrush=Brushes.OrangeRed;}apply.IsEnabled=invalid.Count==0;RefreshPreview();};return field;
    }
    private void Field(Panel panel,string name,UIElement value){System.Windows.Automation.AutomationProperties.SetName(value,name);panel.Children.Add(MonitorUi.Text(name,12));panel.Children.Add(value);}
    private TextBox Number(double number,double min,double max,Action<double> set)=>StringField(number.ToString("0.##",CultureInfo.CurrentCulture),text=>{TryNumber(text,out var v);set(v);},text=>TryNumber(text,out var v)&&v>=min&&v<=max);
    private static bool TryNumber(string text,out double value)=>double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value);
    private TextBox ColorField(string value,Action<string> set,bool optional=false)=>StringField(value,set,s=>optional&&s.Length==0||System.Text.RegularExpressions.Regex.IsMatch(s,"^#[0-9A-Fa-f]{6}$"));
    private void BuildEditor()
    {
        double offset=editorScroller.VerticalOffset;
        var offsets=detailScrollers.ToDictionary(p=>p.Key,p=>p.Value.VerticalOffset);detailScrollers.Clear();
        string focus=Keyboard.FocusedElement is DependencyObject focused?AutomationProperties.GetName(focused):"";
        invalid.Clear();verticalControls.Clear();error.Text="";
        var root=new StackPanel();bool restoring=true;
        root.RequestBringIntoView+=(_,e)=>{if(restoring)e.Handled=true;};
        editorScroller.Content=root;
        root.Children.Add(MonitorUi.Text("Показатели",18));
        root.Children.Add(Composition());
        var layout=new Grid{Margin=new Thickness(0,6,0,12)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(110)});
        var direction=new StackPanel{Margin=new Thickness(0,0,12,0)};
        Field(direction,"Расположение",MonitorUi.Combo(new[]{"Столбцом","В строку"},P.Direction=="Inline"?"В строку":"Столбцом",v=>{P.Direction=v=="В строку"?"Inline":"Rows";foreach(var control in verticalControls)control.IsEnabled=P.Direction!="Inline";RefreshPreview();}));layout.Children.Add(direction);
        var font=new StackPanel();Grid.SetColumn(font,1);Field(font,"Размер текста",Number(P.FontSize,10,64,v=>P.FontSize=v));layout.Children.Add(font);root.Children.Add(layout);
        var tabs=new TabControl{Height=330,Margin=new Thickness(0,10,0,0)};
        tabs.Items.Add(new TabItem{Header="Оформление",Content=Appearance()});
        tabs.Items.Add(new TabItem{Header="Положение",Content=Position()});
        tabs.Items.Add(new TabItem{Header="Профили",Content=Profiles()});
        tabs.SelectedIndex=selectedTab;tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,tabs))selectedTab=tabs.SelectedIndex;};
        var advanced=new Expander{Header="Дополнительные настройки",Content=tabs,IsExpanded=advancedOpen};advanced.Expanded+=(_,_)=>advancedOpen=true;advanced.Collapsed+=(_,_)=>advancedOpen=false;root.Children.Add(advanced);
        RefreshPreview();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{
            if(!ReferenceEquals(editorScroller.Content,root))return;
            if(focus.Length>0)FindNamed(root,focus)?.Focus();
            foreach(var pair in offsets)if(detailScrollers.TryGetValue(pair.Key,out var scroller))scroller.ScrollToVerticalOffset(pair.Value);
            editorScroller.ScrollToVerticalOffset(offset);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>restoring=false));
        }));
    }
    private static FrameworkElement? FindNamed(DependencyObject root,string name)
    {
        if(root is FrameworkElement field&&AutomationProperties.GetName(field)==name)return field;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())if(FindNamed(child,name) is {} match)return match;
        return null;
    }
    private ScrollViewer Details(string key,UIElement content)
    {
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};detailScrollers[key]=scroll;return scroll;
    }
    private UIElement Profiles()
    {
        var panel=new StackPanel{Margin=new Thickness(10)};
        if(profiles.Parent is Panel oldParent)oldParent.Children.Remove(profiles);
        Field(panel,"Текущий профиль",profiles);
        var actions=new WrapPanel();foreach(var item in new UIElement[]{MonitorUi.Button("Новый",()=>Add(false)),MonitorUi.Button("Копия",()=>Add(true)),MonitorUi.Button("Удалить",Delete)})actions.Children.Add(item);panel.Children.Add(actions);
        Field(panel,"Название профиля",StringField(P.Name,v=>{P.Name=v;building=true;profiles.Items.Refresh();building=false;},v=>!string.IsNullOrWhiteSpace(v)&&v.Length<=48));
        return Details("profiles",panel);
    }
    private UIElement Composition()
    {
        var root=new StackPanel();
        var addPanel=new StackPanel();var search=new TextBox{Text=sensorSearch,MinHeight=30,Margin=new Thickness(0,0,0,8)};Field(addPanel,"Поиск по устройству или датчику",search);
        var available=new ListBox{Height=130,DisplayMemberPath="Name"};
        void Search(){available.ItemsSource=catalog.Where(m=>(m.Group+" "+m.Name+" "+m.Unit).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).Select(m=>new Choice(m.Id,m.Group+" · "+m.Name+" · "+m.Unit)).OrderBy(m=>m.Name).ToArray();}
        search.TextChanged+=(_,_)=>{sensorSearch=search.Text;Search();};Search();addPanel.Children.Add(available);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,4,10,4)));itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,26d));itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness()));
        var chosen=new ListBox{Height=150,ItemContainerStyle=itemStyle,DisplayMemberPath="Label",Margin=new Thickness(0,6,0,6)};var options=new StackPanel();
        void Rebuild(int index=0){chosen.ItemsSource=null;chosen.ItemsSource=P.Tiles.Select(t=>new TileChoice(t,t.Label.Length>0?t.Label:catalog.FirstOrDefault(v=>v.Id==t.MetricId)?.Name??t.MetricId)).ToArray();chosen.SelectedIndex=Math.Clamp(index,-1,P.Tiles.Count-1);RefreshPreview();}
        void AddMetric(){if(available.SelectedItem is not Choice c)return;if(P.Tiles.Any(t=>t.MetricId==c.Id)){Rebuild(P.Tiles.FindIndex(t=>t.MetricId==c.Id));return;}if(P.Tiles.Count>=64){error.Text="В одной панели можно разместить до 64 показателей.";return;}var metric=catalog.First(v=>v.Id==c.Id);P.Tiles.Add(new(){MetricId=c.Id,Label=MetricText.Label(metric)});Rebuild(P.Tiles.Count-1);}
        available.MouseDoubleClick+=(_,_)=>AddMetric();addPanel.Children.Add(MonitorUi.Button("Добавить выбранный",AddMetric));root.Children.Add(chosen);
        void Move(int delta){int i=chosen.SelectedIndex,j=i+delta;if(i<0||j<0||j>=P.Tiles.Count)return;(P.Tiles[i],P.Tiles[j])=(P.Tiles[j],P.Tiles[i]);Rebuild(j);}
        root.Children.Add(MonitorUi.Row(MonitorUi.Button("↑ Раньше",()=>Move(-1)),MonitorUi.Button("↓ Позже",()=>Move(1)),MonitorUi.Button("Убрать",()=>{int i=chosen.SelectedIndex;if(i<0)return;P.Tiles.RemoveAt(i);Rebuild(Math.Min(i,P.Tiles.Count-1));})));
        var addExpander=new Expander{Header="+ Добавить показатель",Content=addPanel,IsExpanded=addOpen,Margin=new Thickness(0,2,0,8)};addExpander.Expanded+=(_,_)=>addOpen=true;addExpander.Collapsed+=(_,_)=>addOpen=false;root.Children.Add(addExpander);
        root.Children.Add(options);
        void Options()
        {
            // Old row fields are no longer editable; their draft values stay in the profile.
            ClearInvalid(options);options.Children.Clear();
            if(chosen.SelectedItem is not TileChoice c){RefreshPreview();return;}var tile=c.Tile;
            var details=new StackPanel();
            Field(details,"Подпись",StringField(tile.Label,v=>{tile.Label=v;RefreshPreview();},v=>v.Length<=80));
            Field(details,"Размер текста (0 — общий)",Number(tile.FontSize,0,64,v=>tile.FontSize=v==0?0:Math.Max(10,v)));
            Field(details,"Цвет #RRGGBB (пусто — общий)",ColorField(tile.Color,v=>tile.Color=v,true));
            if(P.Presentation=="Card"){
                var ranges=new StackPanel();
                void Ranges(){ClearInvalid(ranges);ranges.Children.Clear();if(tile.View=="Text")return;Field(ranges,"Минимум графика (пусто — авто)",StringField(tile.GraphMin?.ToString(CultureInfo.CurrentCulture)??"",v=>tile.GraphMin=TryNumber(v,out var n)?n:null,v=>v.Length==0||TryNumber(v,out _)));Field(ranges,"Максимум графика (пусто — авто)",StringField(tile.GraphMax?.ToString(CultureInfo.CurrentCulture)??"",v=>tile.GraphMax=TryNumber(v,out var n)?n:null,v=>v.Length==0||TryNumber(v,out _)));}
                Field(details,"Вид",MonitorUi.Combo(new[]{"Текст","Полоса","График"},tile.View=="Graph"?"График":tile.View=="Bar"?"Полоса":"Текст",v=>{tile.View=v=="График"?"Graph":v=="Полоса"?"Bar":"Text";Ranges();RefreshPreview();}));Ranges();details.Children.Add(ranges);
            }
            var expander=new Expander{Header="Подпись и вид выбранного показателя",Content=details,IsExpanded=metricOptionsOpen,Margin=new Thickness(0,0,0,8)};expander.Expanded+=(_,_)=>metricOptionsOpen=true;expander.Collapsed+=(_,_)=>metricOptionsOpen=false;options.Children.Add(expander);
        }
        chosen.SelectionChanged+=(_,_)=>{if(chosen.SelectedItem is TileChoice c)selectedMetric=c.Tile.MetricId;Options();};refreshMetricOptions=Options;
        int initial=P.Tiles.FindIndex(t=>t.MetricId==selectedMetric);Rebuild(initial<0?0:initial);return root;
    }
    private UIElement Appearance()
    {
        var p=new StackPanel{Margin=new Thickness(10)};
        var description=MonitorUi.Text("",11);var background=new StackPanel();TextBox? backgroundColor=null,backgroundOpacity=null;
        void UpdatePresentation(){description.Text=P.Presentation=="Compact"?"Только текст на прозрачном фоне. Настройки графиков сохранены для вида «Карточка».":"Подложка и графики для выбранных показателей.";background.Visibility=P.Presentation=="Card"?Visibility.Visible:Visibility.Collapsed;if(P.Presentation!="Card"){ClearInvalid(background);if(backgroundColor!=null)backgroundColor.Text=P.Background;if(backgroundOpacity!=null)backgroundOpacity.Text=(P.BackgroundOpacity*100).ToString("0.##",CultureInfo.CurrentCulture);}refreshMetricOptions?.Invoke();RefreshPreview();}
        Field(p,"Вид оверлея",MonitorUi.Combo(new[]{"Компактный текст","Карточка с графиками"},P.Presentation=="Card"?"Карточка с графиками":"Компактный текст",v=>{P.Presentation=v=="Карточка с графиками"?"Card":"Compact";UpdatePresentation();}));
        p.Children.Add(description);
        var vertical=new StackPanel();Field(vertical,"Ширина при расположении столбцом · 160–1600",Number(P.Width,160,1600,v=>P.Width=v));
        Field(vertical,"Число столбцов",MonitorUi.Combo(new[]{1,2,3,4},P.Columns,v=>{P.Columns=v;RefreshPreview();}));vertical.IsEnabled=P.Direction!="Inline";verticalControls.Add(vertical);p.Children.Add(vertical);
        Field(p,"Интервал между показателями · 0–30",Number(P.Spacing,0,30,v=>P.Spacing=v));
        Field(p,"Цвет текста #RRGGBB",ColorField(P.Foreground,v=>P.Foreground=v));
        backgroundColor=ColorField(P.Background,v=>P.Background=v);backgroundOpacity=Number(P.BackgroundOpacity*100,0,100,v=>P.BackgroundOpacity=v/100);Field(background,"Цвет фона #RRGGBB",backgroundColor);Field(background,"Непрозрачность фона · 0–100%",backgroundOpacity);p.Children.Add(background);
        var units=new CheckBox{Content="Показывать единицы измерения",IsChecked=P.ShowUnits,Margin=new Thickness(0,4,0,8)};units.Click+=(_,_)=>{P.ShowUnits=units.IsChecked==true;RefreshPreview();};p.Children.Add(units);
        UpdatePresentation();return Details("appearance",p);
    }
    private void ClearInvalid(DependencyObject root){invalid.Remove(root);foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())ClearInvalid(child);}
    private UIElement Position()
    {
        var p=new StackPanel{Margin=new Thickness(10)};
        var screens=Forms.Screen.AllScreens.Select((s,i)=>new Choice(s.DeviceName,$"Монитор {i+1} · {s.Bounds.Width} × {s.Bounds.Height}"+(s.Primary?" · основной":""))).ToList();screens.Insert(0,new("","Основной монитор"));
        if(!screens.Any(s=>s.Id==P.Monitor))screens.Add(new(P.Monitor,"Отключённый монитор · "+P.Monitor));
        var combo=new ComboBox{ItemsSource=screens,DisplayMemberPath="Name",SelectedItem=screens.First(s=>s.Id==P.Monitor),MinHeight=32};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is Choice c)P.Monitor=c.Id;};Field(p,"Показывать на мониторе",combo);
        var x=Number(P.X*100,0,100,v=>P.X=v/100);var y=Number(P.Y*100,0,100,v=>P.Y=v/100);
        Field(p,"Положение по горизонтали · 0–100%",x);Field(p,"Положение по вертикали · 0–100%",y);
        refreshPosition=()=>{x.Text=(P.X*100).ToString("0.##",CultureInfo.CurrentCulture);y.Text=(P.Y*100).ToString("0.##",CultureInfo.CurrentCulture);combo.SelectedItem=screens.FirstOrDefault(s=>s.Id==P.Monitor);};
        void Corner(double horizontal,double vertical){P.X=horizontal;P.Y=vertical;refreshPosition();}
        p.Children.Add(MonitorUi.Row(MonitorUi.Button("↖",()=>Corner(.02,.02)),MonitorUi.Button("↗",()=>Corner(.98,.02)),MonitorUi.Button("↙",()=>Corner(.02,.98)),MonitorUi.Button("↘",()=>Corner(.98,.98))));
        p.Children.Add(MonitorUi.Text("Если сохранённый монитор отключён, панель временно появится на основном. Выбор отключённого монитора сохранится."));
        return Details("position",p);
    }
    private sealed record Choice(string Id,string Name);
    private sealed record TileChoice(MetricTile Tile,string Label);
    private void FitEditor()
    {
        using var scope=OverlayDisplay.Physical();var handle=new System.Windows.Interop.WindowInteropHelper(Owner??this).Handle;var screen=Forms.Screen.FromHandle(handle);double dpi=Math.Max(96,GetDpiForWindow(handle))/96d;
        MaxWidth=Math.Max(320,(screen.WorkingArea.Width-24)/dpi);MaxHeight=Math.Max(240,(screen.WorkingArea.Height-24)/dpi);MinWidth=Math.Min(MinWidth,MaxWidth);MinHeight=Math.Min(MinHeight,MaxHeight);Width=Math.Min(Width,MaxWidth);Height=Math.Min(Height,MaxHeight);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr handle);
}
