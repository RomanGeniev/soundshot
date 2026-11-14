using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class BackgroundColorSettings : StackPanel
{
    private readonly Func<string?> read;
    private readonly Action<string?> apply;
    private readonly TextBox hex = new() { Width = 100, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,6) };
    private readonly Border preview = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0,0,8,0) };
    private readonly TextBlock hint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button reset;
    public BackgroundColorSettings(Func<string?> read, Action<string?> apply)
    {
        this.read=read;this.apply=apply;
        Margin=new Thickness(0,18,0,0);
        Children.Add(new TextBlock{Text="Фон приложения",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        var row=new WrapPanel();
        var choose=Button("",Choose);choose.Margin=new Thickness(0,0,10,6);
        var content=new StackPanel{Orientation=Orientation.Horizontal};preview.SetResourceReference(Border.BorderBrushProperty,"SubTextBrush");content.Children.Add(preview);content.Children.Add(new TextBlock{Text="Выбрать цвет…",VerticalAlignment=VerticalAlignment.Center});choose.Content=content;
        System.Windows.Automation.AutomationProperties.SetName(choose,"Выбрать цвет фона приложения");row.Children.Add(choose);
        System.Windows.Automation.AutomationProperties.SetName(hex,"Цвет фона в формате HEX");hex.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accept();e.Handled=true;}};row.Children.Add(hex);
        row.Children.Add(Button("Применить",Accept));reset=Button("По теме",()=>{apply(null);Refresh();});row.Children.Add(reset);Children.Add(row);
        hint.SetResourceReference(TextBlock.ForegroundProperty,"SubTextBrush");Children.Add(hint);Refresh();
    }
    private static Button Button(string text,Action click)
    {
        var b=new Button{Content=text,Margin=new Thickness(0,0,8,6)};b.SetResourceReference(StyleProperty,"GhostButton");b.Click+=(_,_)=>click();return b;
    }
    private Color CurrentColor => ThemeManager.TryCustomColor(read(),out var color)?color:(Application.Current.TryFindResource("BgBrush") as SolidColorBrush)?.Color??Colors.Black;
    public void Refresh()
    {
        var color=CurrentColor;hex.Text=$"#{color.R:X2}{color.G:X2}{color.B:X2}";preview.Background=new SolidColorBrush(color);bool custom=ThemeManager.TryCustomColor(read(),out _);reset.IsEnabled=custom;
        hint.Text=custom?"Свой фон. Цвет текста и оттенки карточек подобраны к нему.":"Фон следует теме. Выберите цвет в палитре или введите #RRGGBB.";
    }
    private void Accept()
    {
        if(!ThemeManager.TryCustomColor(hex.Text,out var color)){hint.Text="Введите # и 6 цифр или букв A–F. Например, #172033.";hex.Focus();return;}
        apply($"#{color.R:X2}{color.G:X2}{color.B:X2}");Refresh();
    }
    private void Choose()
    {
        var picker=new AccentColorWindow(Window.GetWindow(this),CurrentColor,"Цвет фона приложения");
        if(picker.ShowDialog()!=true)return;
        apply(picker.SelectedHex);Refresh();
    }
}
