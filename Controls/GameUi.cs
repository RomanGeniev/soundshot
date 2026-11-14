using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

internal static class GameUi
{
    // These compact controls must honor their padding and selected colors. The
    // general application button template reserves padding for full-size actions.
    internal static readonly Style CompactButtonStyle=(Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
          <Setter Property="Background" Value="{DynamicResource GhostBrush}"/>
          <Setter Property="BorderBrush" Value="{DynamicResource CardBorderBrush}"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="FontSize" Value="13"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="surface" CornerRadius="8" Padding="{TemplateBinding Padding}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.85"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.65"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.4"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    public static TextBlock Text(string text,double size=13,bool muted=false){var t=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};t.SetResourceReference(TextBlock.ForegroundProperty,muted?"SubTextBrush":"TextBrush");return t;}
    public static Button Button(string label,Action click,bool primary=false){var b=new Button{Content=label,Padding=new Thickness(10,7,10,7),MinHeight=32,Margin=new Thickness(0,0,5,0),Focusable=false,Style=CompactButtonStyle};if(primary){b.SetResourceReference(Control.BackgroundProperty,"AccentGradient");b.SetResourceReference(Control.BorderBrushProperty,"AccentBrush");b.SetResourceReference(Control.ForegroundProperty,"AccentTextBrush");b.FontWeight=FontWeights.SemiBold;}b.Click+=(_,_)=>click();return b;}
    public static void Select(Button b,bool selected){b.SetResourceReference(Control.BackgroundProperty,selected?"AccentBrush":"GhostBrush");b.SetResourceReference(Control.ForegroundProperty,selected?"AccentSolidTextBrush":"SubTextBrush");b.BorderThickness=new Thickness(1);b.SetResourceReference(Control.BorderBrushProperty,selected?"AccentBrush":"CardBorderBrush");System.Windows.Automation.AutomationProperties.SetItemStatus(b,selected?"Выбрано":"Не выбрано");}
    public static Grid Header(FrameworkElement owner,string title,GameWallet wallet,string rules)
    {
        var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=Text(title,18);name.FontWeight=FontWeights.SemiBold;name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);
        var balance=new WalletBadge(wallet);Grid.SetColumn(balance,1);row.Children.Add(balance);
        var help=Button("?",()=>{var body=new StackPanel();var dialog=Dialog(owner,title+" · правила",body,410);body.Children.Add(Text(rules,13));var close=Button("Понятно",dialog.Close,true);close.Margin=new Thickness(0,16,0,0);body.Children.Add(close);dialog.ShowDialog();});help.Width=28;help.Padding=new Thickness(0);help.Margin=new Thickness(6,0,0,0);System.Windows.Automation.AutomationProperties.SetName(help,"Правила игры "+title);Grid.SetColumn(help,2);row.Children.Add(help);return row;
    }
    public static Window Dialog(FrameworkElement owner,string title,Panel body,double width)
    {
        body.Margin=new Thickness(20);var dialog=new Window{Title=title,Owner=Window.GetWindow(owner),Width=width,SizeToContent=SizeToContent.Height,MaxHeight=620,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};dialog.SetResourceReference(Window.BackgroundProperty,"BgBrush");dialog.SetResourceReference(Window.ForegroundProperty,"TextBrush");dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){dialog.Close();e.Handled=true;}};return dialog;
    }
}

internal sealed class WalletBadge : Button
{
    private readonly GameWallet wallet;
    private readonly TextBlock amount=GameUi.Text("",14);
    public WalletBadge(GameWallet value)
    {
        wallet=value;Style=GameUi.CompactButtonStyle;Padding=new Thickness(9,5,9,5);Focusable=false;MinWidth=105;MinHeight=34;
        var row=new StackPanel{Orientation=Orientation.Horizontal};var coin=new Grid{Width=18,Height=18,Margin=new Thickness(0,0,7,0)};coin.Children.Add(new System.Windows.Shapes.Ellipse{Fill=new SolidColorBrush(Color.FromArgb(35,220,170,70)),Stroke=new SolidColorBrush(Color.FromRgb(207,159,64)),StrokeThickness=1.2});coin.Children.Add(new TextBlock{Text="•",Foreground=new SolidColorBrush(Color.FromRgb(207,159,64)),FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});row.Children.Add(coin);amount.FontWeight=FontWeights.SemiBold;amount.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(amount);row.Children.Add(new TextBlock{Text="›",FontSize=15,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center});Content=row;
        Click+=(_,_)=>ShowHistory();Loaded+=(_,_)=>{wallet.Changed+=Refresh;Refresh();};Unloaded+=(_,_)=>wallet.Changed-=Refresh;Refresh();
    }
    private void Refresh(){amount.Text=wallet.Balance.ToString("N0");System.Windows.Automation.AutomationProperties.SetName(this,$"Общий баланс {wallet.Balance} монет. Открыть историю игр");}
    private void ShowHistory()
    {
        var body=new StackPanel();var window=GameUi.Dialog(this,"Баланс и история",body,420);
        void Draw(){body.Children.Clear();var s=wallet.Snapshot;body.Children.Add(GameUi.Text($"{s.Balance:N0} монет",28));var note=GameUi.Text("Общий баланс для «21» и рулетки",12,true);note.Margin=new Thickness(0,4,0,16);body.Children.Add(note);if(s.TwentyOneStake>0)body.Children.Add(GameUi.Text($"В игре «21»: {s.TwentyOneStake} монет",12,true));if(s.Roulette!=null)body.Children.Add(GameUi.Text($"В рулетке: {s.Roulette.Stake} монет",12,true));
            if(s.Balance==0&&s.TwentyOneStake==0&&s.Roulette==null){var reset=GameUi.Button("Начать заново · 1 000 монет",()=>wallet.Restart(),true);reset.Margin=new Thickness(0,6,0,12);body.Children.Add(reset);}
            if(wallet.Error!=null)body.Children.Add(GameUi.Text(wallet.Error,12));
            foreach(var r in s.History.AsEnumerable().Reverse()){long delta=r.Returned-r.Stake;var row=new Grid{Margin=new Thickness(0,7,0,7)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var labels=new StackPanel();labels.Children.Add(GameUi.Text(r.Game+" · "+r.Result,12));labels.Children.Add(GameUi.Text($"{r.Utc.ToLocalTime():dd.MM HH:mm}   Ставка {r.Stake}",10,true));row.Children.Add(labels);var sum=GameUi.Text((delta>0?"+":"")+delta.ToString("N0"),15);sum.FontWeight=FontWeights.SemiBold;sum.Margin=new Thickness(10,0,0,0);sum.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(sum,1);row.Children.Add(sum);body.Children.Add(row);}
            if(s.History.Count==0)body.Children.Add(GameUi.Text("Пока нет сыгранных партий",13,true));var disclaimer=GameUi.Text("Игровые монеты без покупки и вывода",10,true);disclaimer.Margin=new Thickness(0,15,0,0);body.Children.Add(disclaimer);}
        Draw();wallet.Changed+=Draw;try{window.ShowDialog();}finally{wallet.Changed-=Draw;}
    }
}


