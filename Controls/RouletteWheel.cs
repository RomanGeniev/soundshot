using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GraphiteCompanion.Services;

namespace GraphiteCompanion.Controls;

public sealed class RouletteWheel : Grid
{
    private readonly WheelFace face=new();
    private readonly RotateTransform rotation=new();
    private EventHandler? completed;
    private DoubleAnimation? animation;
    public double Angle {get=>rotation.Angle;set{Stop();rotation.Angle=value;}}
    public int GeometryBuilds=>face.Builds;
    public RouletteWheel()
    {
        face.RenderTransformOrigin=new Point(.5,.5);face.RenderTransform=rotation;face.CacheMode=new BitmapCache{RenderAtScale=2};Children.Add(face);
        var hub=new Border{Width=67,Height=67,CornerRadius=new CornerRadius(40),BorderThickness=new Thickness(2),BorderBrush=new SolidColorBrush(Color.FromRgb(173,140,83)),Background=new SolidColorBrush(Color.FromRgb(20,39,37)),IsHitTestVisible=false,Child=new TextBlock{Text="0–36",FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(225,205,163)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};Children.Add(hub);
        var pointer=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 0,0 L 12,0 L 6,10 Z"),Fill=new SolidColorBrush(Color.FromRgb(247,218,155)),Stroke=new SolidColorBrush(Color.FromRgb(112,85,42)),StrokeThickness=1,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,1,0,0),IsHitTestVisible=false};Children.Add(pointer);
    }
    public static double TargetAngle(int number)=>(360-Array.IndexOf(OverlayRandom.Wheel,number)*360d/37)%360;
    public void ShowNumber(int number){Stop();rotation.Angle=TargetAngle(number);}
    public void SpinTo(int number,Action done)
    {
        double from=rotation.Angle%360;Stop();double target=from+1080+(TargetAngle(number)-from+360)%360;rotation.Angle=target;
        animation=new DoubleAnimation(from,target,TimeSpan.FromSeconds(2.8)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
        completed=(_,_)=>{Stop();done();};animation.Completed+=completed;rotation.BeginAnimation(RotateTransform.AngleProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    public void Stop(){if(animation!=null&&completed!=null)animation.Completed-=completed;animation=null;completed=null;rotation.BeginAnimation(RotateTransform.AngleProperty,null);}
    private sealed class WheelFace : FrameworkElement
    {
        public int Builds {get;private set;}
        protected override void OnRender(DrawingContext dc)
        {
            Builds++;double radius=Math.Max(1,Math.Min(ActualWidth,ActualHeight)/2-4);var center=new Point(ActualWidth/2,ActualHeight/2);double step=360d/37;var gold=new SolidColorBrush(Color.FromRgb(173,140,83));gold.Freeze();dc.DrawEllipse(gold,null,center,radius+3,radius+3);
            for(int i=0;i<37;i++){int n=OverlayRandom.Wheel[i];double a=(i*step-step/2-90)*Math.PI/180,b=(i*step+step/2-90)*Math.PI/180;var shape=new StreamGeometry();using(var c=shape.Open()){c.BeginFigure(center,true,true);c.LineTo(center+new Vector(Math.Cos(a)*radius,Math.Sin(a)*radius),true,false);c.ArcTo(center+new Vector(Math.Cos(b)*radius,Math.Sin(b)*radius),new Size(radius,radius),0,false,SweepDirection.Clockwise,true,false);}shape.Freeze();var fill=new SolidColorBrush(n==0?Color.FromRgb(32,112,77):OverlayRandom.IsRed(n)?Color.FromRgb(168,43,55):Color.FromRgb(27,33,44));fill.Freeze();dc.DrawGeometry(fill,new Pen(gold,.45),shape);
                var text=new FormattedText(n.ToString(),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),7.5,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);double mid=(i*step-90)*Math.PI/180;var p=center+new Vector(Math.Cos(mid)*(radius-9),Math.Sin(mid)*(radius-9));dc.DrawText(text,new Point(p.X-text.Width/2,p.Y-text.Height/2));}
        }
    }
}
