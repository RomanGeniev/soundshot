using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class NumberRandomizerCard : Border
{
    public NumberRandomizerCard(OverlayGamesState state)
    {
        Padding=new Thickness(12);CornerRadius=new CornerRadius(12);SetResourceReference(BackgroundProperty,"InsetBrush");var body=new StackPanel();body.Children.Add(MonitorUi.Text("СЛУЧАЙНОЕ ЧИСЛО",12));
        var result=MonitorUi.Text(state.LastNumber?.ToString()??"—",32);result.FontWeight=FontWeights.SemiBold;body.Children.Add(result);
        var range=MonitorUi.Text($"От {state.Minimum} до {state.Maximum}",12);body.Children.Add(range);
        var actions=new WrapPanel();actions.Children.Add(MonitorUi.Button("Сгенерировать",()=>{state.LastNumber=OverlayRandom.Number(state.Minimum,state.Maximum);result.Text=state.LastNumber.ToString();},true));
        actions.Children.Add(MonitorUi.Button("Диапазон…",()=>
        {
            // Text entry is an explicit owned dialog. The normal overlay stays NOACTIVATE.
            var content=new StackPanel{Margin=new Thickness(18)};var dialog=new Window{Title="Диапазон случайных чисел",Owner=Window.GetWindow(this),Width=340,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Content=content};dialog.SetResourceReference(Window.BackgroundProperty,"BgBrush");dialog.SetResourceReference(Window.ForegroundProperty,"TextBrush");
            TextBox min=new(){Text=state.Minimum.ToString(),MaxLength=11,Margin=new Thickness(0,0,0,10)},max=new(){Text=state.Maximum.ToString(),MaxLength=11,Margin=new Thickness(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetName(min,"От какого числа");System.Windows.Automation.AutomationProperties.SetName(max,"До какого числа");content.Children.Add(MonitorUi.Text("От (включительно)"));content.Children.Add(min);content.Children.Add(MonitorUi.Text("До (включительно)"));content.Children.Add(max);var error=MonitorUi.Text("",11);content.Children.Add(error);
            content.Children.Add(MonitorUi.Row(MonitorUi.Button("Применить",()=>{if(!int.TryParse(min.Text,out int lo)||!int.TryParse(max.Text,out int hi)||lo>hi){error.Text="Введите целые числа: «от» ≤ «до».";return;}state.Minimum=lo;state.Maximum=hi;range.Text=$"От {lo} до {hi}";dialog.Close();},true),MonitorUi.Button("Отмена",()=>dialog.Close())));dialog.ShowDialog();
        }));body.Children.Add(actions);Child=body;
    }
}
