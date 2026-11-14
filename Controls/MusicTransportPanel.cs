using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

// The panel contributes no desired size: artwork and metadata own banner geometry.
// Controls keep natural dimensions and right alignment until the user places them.
public sealed class MusicTransportPanel : Canvas
{
    public Dictionary<string,CanvasPosition> Positions {get;set;}=new();
    public Dictionary<FrameworkElement,string> Keys {get;}=new();
    protected override Size MeasureOverride(Size constraint){foreach(UIElement child in Children)child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));return new();}
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children=Children.Cast<FrameworkElement>().Where(c=>c.Visibility!=Visibility.Collapsed).ToArray();double x=Math.Max(0,finalSize.Width-children.Sum(c=>c.DesiredSize.Width));
        foreach(var child in children){var element=child is EditTile tile?tile.Element:child;double w=Math.Min(finalSize.Width,child.DesiredSize.Width),h=Math.Min(finalSize.Height,child.DesiredSize.Height);double left=x,top=Math.Max(0,(Math.Min(80,finalSize.Height)-h)/2);
            if(Keys.TryGetValue(element,out var key)&&Positions.TryGetValue(key,out var pos)){left=Math.Clamp(pos.X,0,1)*Math.Max(0,finalSize.Width-w);top=Math.Clamp(pos.Y,0,1)*Math.Max(0,finalSize.Height-h);}child.Arrange(new Rect(left,top,w,h));x+=w;}
        return finalSize;
    }
}
public sealed class MusicPlacementEditor : StackPanel
{
    private readonly Canvas preview=new(){Height=160,MinWidth=260,Background=new SolidColorBrush(Color.FromRgb(20,29,43))};
    private readonly Dictionary<string,Button> buttons=new();
    private Dictionary<string,CanvasPosition> positions;
    private readonly Action<Dictionary<string,CanvasPosition>> changed;
    private string? moving;
    private Point offset;
    public MusicPlacementEditor(Dictionary<string,CanvasPosition> value,Action<Dictionary<string,CanvasPosition>> change)
    {
        positions=Copy(value);changed=change;Children.Add(MonitorUi.Text("Расположение кнопок в музыкальном баннере",13));Children.Add(MonitorUi.Text("Перетащите каждую кнопку на схеме. Изменения сохранятся вместе с видом оверлея.",12));
        var caption=new TextBlock{Text="Обложка       Название трека\n                    Исполнитель",Foreground=Brushes.SlateGray,Margin=new Thickness(12),FontSize=12,IsHitTestVisible=false};preview.Children.Add(caption);Children.Add(preview);
        foreach(var (key,label) in new[]{("previous","⏮"),("play","▶"),("next","⏭")}){var button=new Button{Content=label,Width=key=="play"?52:42,Height=key=="play"?52:42,Cursor=Cursors.SizeAll,ToolTip="Перетащить "+label};button.SetResourceReference(StyleProperty,key=="play"?"RoundAccentButton":"RoundGhostButton");buttons[key]=button;preview.Children.Add(button);
            button.PreviewMouseLeftButtonDown+=(_,e)=>{moving=key;offset=e.GetPosition(button);button.CaptureMouse();e.Handled=true;};
            button.PreviewMouseMove+=(_,e)=>{if(moving!=key||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(preview);positions[key]=new(){X=Math.Clamp((p.X-offset.X)/Math.Max(1,preview.ActualWidth-button.Width),0,1),Y=Math.Clamp((p.Y-offset.Y)/Math.Max(1,preview.ActualHeight-button.Height),0,1)};Draw();changed(Copy(positions));e.Handled=true;};
            button.PreviewMouseLeftButtonUp+=(_,e)=>{if(moving!=key)return;moving=null;button.ReleaseMouseCapture();e.Handled=true;};button.LostMouseCapture+=(_,_)=>moving=null;
        }
        preview.SizeChanged+=(_,_)=>Draw();Children.Add(MonitorUi.Button("Вернуть кнопки вправо",()=>{positions.Clear();Draw();changed(Copy(positions));}));
    }
    public void Reset(Dictionary<string,CanvasPosition> value){positions=Copy(value);Draw();}
    private static Dictionary<string,CanvasPosition> Copy(Dictionary<string,CanvasPosition>? value)=>(value??new()).ToDictionary(p=>p.Key,p=>new CanvasPosition{X=p.Value.X,Y=p.Value.Y});
    private void Draw(){double x=Math.Max(0,preview.ActualWidth-152);foreach(var p in buttons){var button=p.Value;double left=x,top=(Math.Min(80,preview.ActualHeight)-button.Height)/2;if(positions.TryGetValue(p.Key,out var pos)){left=pos.X*Math.Max(0,preview.ActualWidth-button.Width);top=pos.Y*Math.Max(0,preview.ActualHeight-button.Height);}SetLeft(button,left);SetTop(button,top);x+=button.Width+8;} }
    private static void SetLeft(UIElement child,double value)=>Canvas.SetLeft(child,value);
    private static void SetTop(UIElement child,double value)=>Canvas.SetTop(child,value);
}
