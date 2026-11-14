using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class MetricPlot : FrameworkElement
{
    private double?[] samples=Array.Empty<double?>();
    public Brush Stroke {get;set;}=Brushes.DeepSkyBlue;
    public double? Minimum {get;set;}
    public double? Maximum {get;set;}
    public bool Bar {get;set;}
    public void SetValues(double?[] values){samples=values;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(24,160,180,200)),null,new Rect(0,0,w,h));
        var valid=samples.Where(x=>x.HasValue&&double.IsFinite(x.Value)).Select(x=>x!.Value).ToArray();if(valid.Length==0)return;
        double lo=Minimum??Math.Min(0,valid.Min()),hi=Maximum??Math.Max(lo+1,valid.Max()*1.1);if(hi<=lo)hi=lo+1;
        double Y(double v)=>h-2-Math.Clamp((v-lo)/(hi-lo),0,1)*Math.Max(0,h-4);
        if(Bar){var last=samples.LastOrDefault();if(last.HasValue)dc.DrawRectangle(Stroke,null,new Rect(0,0,w*Math.Clamp((last.Value-lo)/(hi-lo),0,1),h));return;}
        var pen=new Pen(Stroke,1.5);Point? previous=null;
        for(int i=0;i<samples.Length;i++){if(samples[i] is not { } v||!double.IsFinite(v)){previous=null;continue;}var p=new Point(samples.Length>1?w*i/(samples.Length-1):w,Y(v));if(previous.HasValue)dc.DrawLine(pen,previous.Value,p);previous=p;}
    }
}
public sealed class MetricView : Border
{
    private sealed record Row(MetricTile Tile,TextBlock Label,TextBlock Value,MetricPlot? Plot);
    private readonly List<Row> rows=new();
    private MonitorProfile profile=new();
    public void Apply(MonitorProfile value)
    {
        profile=value.Normalize();Width=profile.Width;Padding=new Thickness(12);CornerRadius=new CornerRadius(8);
        var background=(Color)ColorConverter.ConvertFromString(profile.Background);background.A=(byte)Math.Round(profile.BackgroundOpacity*255);Background=new SolidColorBrush(background);
        rows.Clear();var grid=new Grid();for(int c=0;c<profile.Columns;c++)grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Child=grid;
        if(profile.Tiles.Count==0){grid.Children.Add(new TextBlock{Text="Показатели не выбраны",Foreground=Brush(profile.Foreground),FontSize=profile.FontSize,Margin=new Thickness(4)});return;}
        int index=0;foreach(var tile in profile.Tiles)
        {
            var color=Brush(tile.Color.Length>0?tile.Color:profile.Foreground);double size=tile.FontSize>0?tile.FontSize:profile.FontSize;
            var cell=new StackPanel{Margin=new Thickness(4,profile.Spacing/2,4,profile.Spacing/2)};
            var line=new Grid();line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});line.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var title=new TextBlock{Text=tile.Label,Foreground=color,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            var current=new TextBlock{Text="—",Foreground=color,FontSize=size,FontWeight=FontWeights.SemiBold,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(current,1);line.Children.Add(title);line.Children.Add(current);cell.Children.Add(line);
            MetricPlot? plot=null;if(tile.View!="Text"){plot=new(){Height=tile.View=="Bar"?5:34,Margin=new Thickness(0,5,0,0),Stroke=color,Bar=tile.View=="Bar",Minimum=tile.GraphMin,Maximum=tile.GraphMax};cell.Children.Add(plot);}
            if(index%profile.Columns==0)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(cell,index/profile.Columns);Grid.SetColumn(cell,index%profile.Columns);index++;grid.Children.Add(cell);rows.Add(new(tile,title,current,plot));
        }
    }
    public void Update(MetricSnapshot? snapshot,MetricHistory history)
    {
        var values=snapshot?.Values.GroupBy(v=>v.Id).ToDictionary(g=>g.Key,g=>g.Last())??new();bool stale=snapshot==null||DateTime.UtcNow-snapshot.Timestamp>TimeSpan.FromSeconds(10);
        foreach(var row in rows)
        {
            values.TryGetValue(row.Tile.MetricId,out var metric);row.Label.Text=row.Tile.Label.Length>0?row.Tile.Label:metric?.Name??"Датчик недоступен";
            row.Value.Text=MetricText.Format(stale?null:metric?.Value,metric?.Unit??"",profile.ShowUnits);
            row.Label.ToolTip=metric==null?row.Tile.MetricId+" · датчик недоступен":metric.Group+" · "+metric.Name;
            if(row.Plot!=null){row.Plot.Minimum=row.Tile.GraphMin??(metric?.Unit=="%"?0:null);row.Plot.Maximum=row.Tile.GraphMax??(metric?.Unit=="%"?100:null);row.Plot.SetValues(stale||metric?.Value==null?Array.Empty<double?>():history.Get(row.Tile.MetricId));}
        }
    }
    private static Brush Brush(string color){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));b.Freeze();return b;}
}
