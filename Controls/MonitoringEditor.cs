using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraphiteCompanion.Services;
using Forms=System.Windows.Forms;

namespace GraphiteCompanion.Controls;

internal static class MonitorUi
{
    public static TextBlock Text(string value,double size=13){var t=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,FontSize=size,Margin=new Thickness(0,3,0,6)};t.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");return t;}
    public static Button Button(string text,Action click,bool accent=false){var b=new Button{Content=text,Margin=new Thickness(0,0,6,6),Padding=new Thickness(12,8,12,8)};b.SetResourceReference(FrameworkElement.StyleProperty,accent?"AccentButton":"GhostButton");b.Click+=(_,_)=>click();return b;}
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
    private readonly ContentControl editor=new();
    private readonly ComboBox profiles=new(){MinWidth=180};
    private readonly TextBlock error=MonitorUi.Text("");
    private readonly Button apply;
    private MetricSnapshot? snapshot;
    private MetricHistory history=new();
    private readonly HashSet<object> invalid=new();
    private MonitorProfile P=>draft.Current;
    private bool building;
    public bool Saved {get;private set;}
    public MonitoringSettings Draft=>draft.Copy();
    public MonitoringEditor(MonitoringSettings current,IEnumerable<MetricValue> available,Func<MonitoringSettings,string?> commit,Action<MonitorProfile,Action<MonitorProfile?>> placement)
    {
        draft=current.Normalize();save=commit;place=placement;catalog=available.GroupBy(m=>m.Id).Select(g=>g.Last()).ToList();
        Title="Настроить экранную панель";Width=1030;Height=760;MinWidth=600;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=13;
        SourceInitialized+=(_,_)=>FitEditor();
        SetResourceReference(BackgroundProperty,"BgBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        apply=MonitorUi.Button("Сохранить",Commit,true);
        var bottom=MonitorUi.Row(apply,MonitorUi.Button("Отмена",Close));bottom.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        profiles.DisplayMemberPath="Name";profiles.SelectionChanged+=(_,_)=>{if(building||profiles.SelectedItem is not MonitorProfile p)return;draft.ActiveProfile=p.Id;BuildEditor();};
        var top=new WrapPanel();foreach(var item in new UIElement[]{profiles,MonitorUi.Button("Новый",()=>Add(false)),MonitorUi.Button("Копия",()=>Add(true)),MonitorUi.Button("Удалить профиль",Delete)})top.Children.Add(item);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(350)});root.Children.Add(body);
        SizeChanged+=(_,_)=>body.ColumnDefinitions[1].Width=new GridLength(ActualWidth<900?230:350);
        editor.Margin=new Thickness(0,12,16,0);body.Children.Add(editor);
        var previewColumn=new StackPanel{Margin=new Thickness(0,12,0,0)};var previewScroll=new ScrollViewer{Content=previewColumn,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(previewScroll,1);body.Children.Add(previewScroll);
        previewColumn.Children.Add(MonitorUi.Text("ПРЕДПРОСМОТР",12));
        var backdrop=new Border{Background=new SolidColorBrush(Color.FromRgb(55,65,78)),Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Height=330};
        backdrop.Child=new Viewbox{Child=preview,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top};previewColumn.Children.Add(backdrop);
        previewColumn.Children.Add(MonitorUi.Text("Предпросмотр уменьшен, если панель не помещается. На экране применяется выбранный размер."));
        previewColumn.Children.Add(MonitorUi.Button("Разместить на экране",()=>{if(invalid.Count>0)return;Hide();place(P.Copy(),result=>{if(result!=null){P.X=result.X;P.Y=result.Y;P.Monitor=result.Monitor;}Show();Activate();BuildEditor();});}));
        previewColumn.Children.Add(MonitorUi.Text("В размещении: перетащите панель, Enter — принять, Esc — отменить. В обычном режиме панель пропускает клики."));
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
    private void RefreshPreview(){preview.Apply(P);preview.Update(snapshot,history);apply.IsEnabled=invalid.Count==0;}
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
        invalid.Clear();error.Text="";
        var root=new DockPanel();editor.Content=root;
        var name=StringField(P.Name,v=>{P.Name=v;building=true;profiles.Items.Refresh();building=false;},v=>!string.IsNullOrWhiteSpace(v)&&v.Length<=48);System.Windows.Automation.AutomationProperties.SetName(name,"Название профиля");DockPanel.SetDock(name,Dock.Top);root.Children.Add(name);
        var tabs=new TabControl();root.Children.Add(tabs);
        tabs.Items.Add(new TabItem{Header="Показатели",Content=Composition()});
        tabs.Items.Add(new TabItem{Header="Оформление",Content=Appearance()});
        tabs.Items.Add(new TabItem{Header="Монитор и позиция",Content=Position()});
        RefreshPreview();
    }
    private UIElement Composition()
    {
        var root=new StackPanel{Margin=new Thickness(10)};
        var search=new TextBox{MinHeight=30,Margin=new Thickness(0,0,0,8)};Field(root,"Найти показатель",search);
        var available=new ListBox{Height=130,DisplayMemberPath="Name"};
        void Search(){available.ItemsSource=catalog.Where(m=>(m.Group+" "+m.Name).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).Select(m=>new Choice(m.Id,m.Group+" · "+m.Name)).OrderBy(m=>m.Name).ToArray();}
        search.TextChanged+=(_,_)=>Search();Search();root.Children.Add(available);
        var chosen=new ListBox{Height=130,DisplayMemberPath="Label",Margin=new Thickness(0,6,0,6)};var options=new StackPanel();
        void Rebuild(int index=0){chosen.ItemsSource=null;chosen.ItemsSource=P.Tiles.Select(t=>new TileChoice(t,t.Label.Length>0?t.Label:catalog.FirstOrDefault(v=>v.Id==t.MetricId)?.Name??t.MetricId)).ToArray();chosen.SelectedIndex=Math.Clamp(index,-1,P.Tiles.Count-1);if(chosen.SelectedItem!=null)chosen.ScrollIntoView(chosen.SelectedItem);RefreshPreview();}
        void AddMetric(){if(available.SelectedItem is not Choice c)return;if(P.Tiles.Count>=64){error.Text="В одной панели можно разместить до 64 показателей.";return;}P.Tiles.Add(new(){MetricId=c.Id,Label=catalog.First(v=>v.Id==c.Id).Name});Rebuild(P.Tiles.Count-1);}
        available.MouseDoubleClick+=(_,_)=>AddMetric();root.Children.Add(MonitorUi.Button("Добавить в панель",AddMetric));root.Children.Add(MonitorUi.Text("НА ПАНЕЛИ · порядок чтения по строкам",12));root.Children.Add(chosen);
        void Move(int delta){int i=chosen.SelectedIndex,j=i+delta;if(i<0||j<0||j>=P.Tiles.Count)return;(P.Tiles[i],P.Tiles[j])=(P.Tiles[j],P.Tiles[i]);Rebuild(j);}
        root.Children.Add(MonitorUi.Row(MonitorUi.Button("↑ Выше",()=>Move(-1)),MonitorUi.Button("↓ Ниже",()=>Move(1)),MonitorUi.Button("Убрать",()=>{int i=chosen.SelectedIndex;if(i<0)return;P.Tiles.RemoveAt(i);Rebuild(Math.Min(i,P.Tiles.Count-1));})));
        root.Children.Add(options);
        chosen.SelectionChanged+=(_,_)=>
        {
            // Old row fields are no longer editable; their draft values stay in the profile.
            foreach(var f in options.Children.OfType<TextBox>())invalid.Remove(f);options.Children.Clear();
            if(chosen.SelectedItem is not TileChoice c){RefreshPreview();return;}var tile=c.Tile;
            Field(options,"Подпись",StringField(tile.Label,v=>{tile.Label=v;RefreshPreview();},v=>v.Length<=80));
            Field(options,"Вид",MonitorUi.Combo(new[]{"Текст","Полоса","График"},tile.View=="Graph"?"График":tile.View=="Bar"?"Полоса":"Текст",v=>{tile.View=v=="График"?"Graph":v=="Полоса"?"Bar":"Text";RefreshPreview();}));
            Field(options,"Размер текста (0 — общий)",Number(tile.FontSize,0,64,v=>tile.FontSize=v==0?0:Math.Max(10,v)));
            Field(options,"Цвет #RRGGBB (пусто — общий)",ColorField(tile.Color,v=>tile.Color=v,true));
            Field(options,"Нижняя граница графика (пусто — автоматически)",StringField(tile.GraphMin?.ToString(CultureInfo.CurrentCulture)??"",v=>tile.GraphMin=TryNumber(v,out var n)?n:null,v=>v.Length==0||TryNumber(v,out _)));
            Field(options,"Верхняя граница графика (пусто — автоматически)",StringField(tile.GraphMax?.ToString(CultureInfo.CurrentCulture)??"",v=>tile.GraphMax=TryNumber(v,out var n)?n:null,v=>v.Length==0||TryNumber(v,out _)));
        };
        Rebuild();return new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private UIElement Appearance()
    {
        var p=new StackPanel{Margin=new Thickness(10)};
        Field(p,"Общий размер текста · 10–64",Number(P.FontSize,10,64,v=>P.FontSize=v));
        Field(p,"Ширина панели · 160–1600",Number(P.Width,160,1600,v=>P.Width=v));
        Field(p,"Колонки",MonitorUi.Combo(new[]{1,2,3,4},P.Columns,v=>{P.Columns=v;RefreshPreview();}));
        Field(p,"Расстояние между строками · 0–30",Number(P.Spacing,0,30,v=>P.Spacing=v));
        Field(p,"Цвет текста #RRGGBB",ColorField(P.Foreground,v=>P.Foreground=v));
        Field(p,"Цвет фона #RRGGBB",ColorField(P.Background,v=>P.Background=v));
        Field(p,"Непрозрачность фона · 0–100%",Number(P.BackgroundOpacity*100,0,100,v=>P.BackgroundOpacity=v/100));
        var units=new CheckBox{Content="Показывать единицы измерения",IsChecked=P.ShowUnits,Margin=new Thickness(0,4,0,8)};units.Click+=(_,_)=>{P.ShowUnits=units.IsChecked==true;RefreshPreview();};p.Children.Add(units);
        p.Children.Add(MonitorUi.Button("Компактный текст",()=>{P.FontSize=14;P.Spacing=3;foreach(var t in P.Tiles)t.View="Text";BuildEditor();}));
        return new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private UIElement Position()
    {
        var p=new StackPanel{Margin=new Thickness(10)};
        var screens=Forms.Screen.AllScreens.Select((s,i)=>new Choice(s.DeviceName,$"Монитор {i+1} · {s.Bounds.Width} × {s.Bounds.Height}"+(s.Primary?" · основной":""))).ToList();screens.Insert(0,new("","Основной монитор"));
        if(!screens.Any(s=>s.Id==P.Monitor))screens.Add(new(P.Monitor,"Отключённый монитор · "+P.Monitor));
        var combo=new ComboBox{ItemsSource=screens,DisplayMemberPath="Name",SelectedItem=screens.First(s=>s.Id==P.Monitor),MinHeight=32};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is Choice c)P.Monitor=c.Id;};Field(p,"Показывать на мониторе",combo);
        Field(p,"Положение по горизонтали · 0–100%",Number(P.X*100,0,100,v=>P.X=v/100));
        Field(p,"Положение по вертикали · 0–100%",Number(P.Y*100,0,100,v=>P.Y=v/100));
        p.Children.Add(MonitorUi.Row(MonitorUi.Button("↖",()=>{P.X=P.Y=.02;BuildEditor();}),MonitorUi.Button("↗",()=>{P.X=.98;P.Y=.02;BuildEditor();}),MonitorUi.Button("↙",()=>{P.X=.02;P.Y=.98;BuildEditor();}),MonitorUi.Button("↘",()=>{P.X=P.Y=.98;BuildEditor();})));
        p.Children.Add(MonitorUi.Text("Если сохранённый монитор отключён, панель временно появится на основном. Выбор отключённого монитора сохранится."));
        return new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
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
