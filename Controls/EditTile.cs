using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace GraphiteCompanion.Controls;

// Chrome overlays the original layout. The closest wrapper owns input, including
// nested controls, so moving a capture/audio button never invokes its real action.
public sealed class EditTile : Border
{
    internal sealed record Payload(object Scope,string Id,string Name,double Height);
    public FrameworkElement Element {get;}
    internal bool HasLayoutContent => Element.Visibility != Visibility.Collapsed && Element.DesiredSize.Height > .01;
    private readonly Grid surface=new();
    private readonly Grid proportionalContent=new();
    private readonly UniformBlockHost proportionalHost=new();
    private readonly OverlayChrome chrome=new();
    private readonly Border outline;
    private readonly Rectangle landing;
    private readonly StackPanel actions=new(){Orientation=Orientation.Horizontal};
    private readonly TextBlock moveGrip;
    private readonly List<Thumb> resizeGrips=new();
    private readonly object scope;
    private readonly string id,name;
    private readonly Action? insideAction,sizeAction;
    private readonly double naturalWidth,naturalHeight;
    private readonly VerticalAlignment naturalAlignment;
    private static Thumb? activeResize;
    internal static bool CancelActiveResize(){if(activeResize?.IsDragging!=true)return false;activeResize.CancelDrag();return true;}
    private bool selected,editable,dragging;
    private double elementOpacity=1;
    private LayoutSize size=new();
    private LayoutSize? resizeBefore;
    private KeyboardNavigationMode originalNavigation;
    public bool InlineActions {get;set;}=true;
    public bool ShowResizeGrip {get;set;}=true;
    public bool IsContainer {get;set;}
    public bool HasOptions {get;set;}
    public string Title=>name;
    public string InsideLabel {get;set;}="Содержимое";
    public bool Editable=>editable;
    internal string Id=>id;
    internal object Scope=>scope;
    internal Point DragGrab {get;private set;}
    internal bool ResizeFromLeft {get;private set;}
    internal bool ResizeFromTop {get;private set;}
    internal event Action<Vector>? Nudge;
    internal double PreferredWidth=>size.Width;
    public event Action<EditTile>? Selected;
    public event Action? DragStarted;
    public event Action<bool>? DragFinished;
    public event Action? ResizeStarted;
    public event Action<bool>? ResizeFinished;
    public event Action<LayoutSize>? SizeChangedByUser;
    public event Action<int>? MoveByKeyboard;
    public EditTile(FrameworkElement element,object scope,string id,string name,Action hide,Action? inside=null,Action? sizing=null)
    {
        Element=element;this.scope=scope;this.id=id;this.name=name;insideAction=inside;sizeAction=sizing;
        naturalWidth=element.Width;naturalHeight=element.Height;naturalAlignment=element.VerticalAlignment;Background=Brushes.Transparent;Focusable=true;surface.Children.Add(element);chrome.Outer=scope is SnapBoard;
        outline=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),IsHitTestVisible=false};outline.SetResourceReference(BorderBrushProperty,"CardBorderBrush");chrome.Children.Add(outline);
        landing=new Rectangle{RadiusX=7,RadiusY=7,StrokeThickness=2,StrokeDashArray=new DoubleCollection{3,2},IsHitTestVisible=false,Visibility=Visibility.Collapsed};landing.SetResourceReference(Shape.StrokeProperty,"AccentBrush");chrome.Children.Add(landing);
        if(inside!=null)actions.Children.Add(IconButton(false,"Настроить: "+name,inside));
        actions.Children.Add(IconButton(true,"Скрыть «"+name+"». Вернуть можно на полке «Скрытое»",hide));chrome.Children.Add(actions);
        moveGrip=new TextBlock{Text="⠿",FontSize=12,Width=12,Height=14,TextAlignment=TextAlignment.Center,Cursor=Cursors.SizeAll,ToolTip="Перетащите весь блок: "+name};moveGrip.SetResourceReference(TextBlock.ForegroundProperty,"SubTextBrush");moveGrip.SetResourceReference(TextBlock.BackgroundProperty,"CardBrush");chrome.Children.Add(moveGrip);
        foreach(var edge in new[]{"w","e","n","s","nw","ne","sw","se"}){var grip=MakeGrip(edge,edge is "w" or "e"?Cursors.SizeWE:edge is "n" or "s"?Cursors.SizeNS:edge is "nw" or "se"?Cursors.SizeNWSE:Cursors.SizeNESW);resizeGrips.Add(grip);chrome.Children.Add(grip);}surface.Children.Add(chrome);Child=surface;
        Point start=default;bool pressed=false;
        PreviewMouseLeftButtonDown+=(_,e)=>{if(!editable||Nearest(e.OriginalSource as DependencyObject)!=this||IsTool(e.OriginalSource as DependencyObject))return;Selected?.Invoke(this);Focus();pressed=true;start=e.GetPosition(this);CaptureMouse();e.Handled=true;};
        PreviewMouseLeftButtonUp+=(_,e)=>{if(!pressed)return;pressed=false;ReleaseMouseCapture();e.Handled=true;};
        PreviewMouseWheel+=(_,e)=>{if(!editable||Nearest(e.OriginalSource as DependencyObject)!=this)return;for(var n=GetParentNode(this);n!=null;n=GetParentNode(n))if(n is SnapBoard board){board.ScrollEditing(e.Delta);break;}e.Handled=true;};
        LostMouseCapture+=(_,_)=>pressed=false;
        MouseMove+=(_,e)=>{
            if(!pressed||!editable||e.LeftButton!=MouseButtonState.Pressed)return;var at=e.GetPosition(this);
            if(Math.Abs(at.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(at.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            pressed=false;ReleaseMouseCapture();bool accepted=false;using var preview=DragPreview.Begin(this,start);
            try{ToolTipService.SetIsEnabled(this,false);DragGrab=start;DragStarted?.Invoke();dragging=true;Element.Opacity=.08;RefreshChrome();accepted=DragDrop.DoDragDrop(this,new DataObject(typeof(Payload),new Payload(scope,id,name,ActualHeight)),DragDropEffects.Move)==DragDropEffects.Move;}
            finally{dragging=false;Element.Opacity=elementOpacity;RefreshChrome();DragFinished?.Invoke(accepted);ToolTipService.SetIsEnabled(this,editable);}
        };
        PreviewKeyDown+=(_,e)=>{
            if(!editable||Nearest(e.OriginalSource as DependencyObject)!=this)return;
            if(e.Key==Key.Escape&&resizeBefore!=null){foreach(var t in resizeGrips)if(t.IsDragging)t.CancelDrag();e.Handled=true;return;}
            if(e.OriginalSource is Thumb)return;
            if(e.Key is Key.Left or Key.Up or Key.Right or Key.Down){if(Nudge!=null)Nudge.Invoke(e.Key switch{Key.Left=>new Vector(-8,0),Key.Right=>new Vector(8,0),Key.Up=>new Vector(0,-8),_=>new Vector(0,8)});else MoveByKeyboard?.Invoke(e.Key is Key.Left or Key.Up?-1:1);e.Handled=true;}else if(e.Key is Key.Enter or Key.Space)e.Handled=true;
        };
        Element.SizeChanged+=(_,_)=>RefreshChrome();MouseEnter+=(_,_)=>RefreshChrome();MouseLeave+=(_,_)=>RefreshChrome();SetEditing(false);
    }
    private bool IsTool(DependencyObject? n){bool tool=false;for(;n!=null&&n!=this;n=GetParentNode(n)){if(n is ButtonBase or Thumb)tool=true;if(n==chrome)return tool;}return false;}
    private static DependencyObject? GetParentNode(DependencyObject n)=>n is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(n):LogicalTreeHelper.GetParent(n);
    private static EditTile? Nearest(DependencyObject? n){for(;n!=null;n=GetParentNode(n))if(n is EditTile t)return t;return null;}
    private Thumb MakeGrip(string edge,Cursor cursor)
    {
        bool side=edge is "w" or "e",horizontal=edge is "n" or "s";
        string label=edge switch{"w"=>"левый край","e"=>"правый край","n"=>"верхний край","s"=>"нижний край","nw"=>"левый верхний угол","ne"=>"правый верхний угол","sw"=>"левый нижний угол",_=>"правый нижний угол"};
        var grip=new Thumb{Tag=edge,Cursor=cursor,ToolTip="Тяните "+label+": "+(side?"ширина":horizontal?"высота":"ширина и высота")};AutomationProperties.SetName(grip,name+": "+label);
        var mark=new FrameworkElementFactory(typeof(Border));mark.SetValue(Border.WidthProperty,horizontal?18d:side?3d:6d);mark.SetValue(Border.HeightProperty,side?18d:horizontal?3d:6d);mark.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));mark.SetValue(Border.BorderThicknessProperty,new Thickness(1));mark.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);mark.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);mark.SetResourceReference(Border.BorderBrushProperty,"SubTextBrush");mark.SetResourceReference(Border.BackgroundProperty,"CardBrush");
        var hitArea=new FrameworkElementFactory(typeof(Grid));hitArea.SetValue(Panel.BackgroundProperty,Brushes.Transparent);hitArea.AppendChild(mark);grip.Template=new ControlTemplate(typeof(Thumb)){VisualTree=hitArea};
        Size bounds=default;Point pointer=default;double fit=1;
        LayoutSize? reference=null;
        bool CanResize()=>scope is SnapBoard&&ShowResizeGrip&&sizeAction!=null&&editable;
        void Capture(){var board=(SnapBoard)scope;fit=board.ResizeFit;bounds=new Size(ActualWidth/fit,ActualHeight/fit);reference=size.Copy();if(reference.Proportional&&!board.HasPositions)reference.Scale=ActualWidth/reference.ReferenceWidth;reference.ReferenceWidth=bounds.Width/reference.Scale;reference.ReferenceHeight=bounds.Height/reference.Scale;reference.Normalize();ResizeFromLeft=edge.Contains('w');ResizeFromTop=edge.Contains('n');}
        grip.DragStarted+=(_,e)=>{if(!CanResize())return;activeResize=grip;Selected?.Invoke(this);resizeBefore=size.Copy();Capture();pointer=Mouse.GetPosition(((SnapBoard)scope).DragSurface);ResizeStarted?.Invoke();e.Handled=true;};
        grip.DragDelta+=(_,e)=>{if(resizeBefore==null||reference==null||!CanResize())return;var delta=(Mouse.GetPosition(((SnapBoard)scope).DragSurface)-pointer)/fit;SizeChangedByUser?.Invoke(BlockResize.Apply(reference,bounds,delta,edge));e.Handled=true;};
        grip.DragCompleted+=(_,e)=>{if(resizeBefore==null)return;if(e.Canceled)SizeChangedByUser?.Invoke(resizeBefore);resizeBefore=null;reference=null;activeResize=null;ResizeFinished?.Invoke(!e.Canceled);ResizeFromLeft=ResizeFromTop=false;e.Handled=true;};
        grip.KeyDown+=(_,e)=>{if(!CanResize()||e.Key is not(Key.Left or Key.Right or Key.Up or Key.Down))return;Capture();var delta=e.Key switch{Key.Left=>new Vector(-8,0),Key.Right=>new Vector(8,0),Key.Up=>new Vector(0,-8),_=>new Vector(0,8)};ResizeStarted?.Invoke();SizeChangedByUser?.Invoke(BlockResize.Apply(reference!,bounds,delta,edge));reference=null;ResizeFinished?.Invoke(true);ResizeFromLeft=ResizeFromTop=false;e.Handled=true;};return grip;
    }
    private static Button IconButton(bool remove,string title,Action action)
    {
        var p=new Path{Width=9,Height=9,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Data=Geometry.Parse(remove?"M3,3 L13,13 M13,3 L3,13":"M2,4 L14,4 M5,1 L5,7 M2,12 L14,12 M11,9 L11,15")};if(remove)p.Stroke=new SolidColorBrush(Color.FromRgb(240,78,100));else p.SetResourceReference(Shape.StrokeProperty,"TextBrush");var b=new Button{Content=p,Width=18,Height=18,MinWidth=0,MinHeight=0,Padding=new Thickness(),Margin=new Thickness(2,0,0,0),ToolTip=title};b.SetResourceReference(StyleProperty,"RoundGhostButton");b.SetResourceReference(BackgroundProperty,"CardBrush");AutomationProperties.SetName(b,title);b.Click+=(_,e)=>{e.Handled=true;action();};return b;
    }
    public void SetSize(LayoutSize? value)
    {
        var next=(value??new()).Copy();next.Normalize();if(size.Scale==next.Scale&&size.Width==next.Width&&size.Height==next.Height&&size.ReferenceWidth==next.ReferenceWidth&&size.ReferenceHeight==next.ReferenceHeight&&size.FreeAspect==next.FreeAspect)return;size=next;
        if(size.Proportional){
            if(Element.Parent==surface){surface.Children.Remove(Element);proportionalContent.Children.Add(Element);proportionalHost.Child=proportionalContent;surface.Children.Insert(0,proportionalHost);}
            surface.LayoutTransform=Transform.Identity;proportionalHost.Configure(size);
        }else{
            if(Element.Parent==proportionalContent){proportionalContent.Children.Remove(Element);surface.Children.Remove(proportionalHost);surface.Children.Insert(0,Element);}
            surface.LayoutTransform=size.Scale==1?Transform.Identity:new ScaleTransform(size.Scale,size.Scale);
        }
        if(Element is ButtonBase){Element.Width=size.Width>0?double.NaN:naturalWidth;Element.Height=size.Height>0?double.NaN:naturalHeight;}Element.VerticalAlignment=size.Height>0?VerticalAlignment.Stretch:naturalAlignment;HorizontalAlignment=size.Width>0?HorizontalAlignment.Left:HorizontalAlignment.Stretch;InvalidateMeasure();
    }
    public double ReadableMinimumWidth{get{double min=Math.Max(48,(Element.MinWidth+Element.Margin.Left+Element.Margin.Right)*size.Scale);if(Element is ButtonBase){Element.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));min=Math.Max(min,Element.DesiredSize.Width*size.Scale);}return Math.Ceiling(min/16)*16;}}
    protected override Size MeasureOverride(Size constraint){if(size.Proportional)return base.MeasureOverride(new Size(Math.Min(constraint.Width,size.Width),double.PositiveInfinity));double width=size.Width>0?Math.Min(constraint.Width,Math.Max(ReadableMinimumWidth,size.Width)):constraint.Width;var measured=base.MeasureOverride(new Size(width,constraint.Height));return new Size(Math.Max(measured.Width,double.IsFinite(width)&&size.Width>0?width:0),Math.Max(measured.Height,size.Height));}
    public FrameworkElement Detach(){((Panel)Element.Parent).Children.Remove(Element);return Element;}
    public void SetInnerAvailable(bool value){IsContainer=value;RefreshChrome();}
    public void SetSelected(bool value){selected=value;RefreshChrome();}
    public FrameworkElement ContextActions(){var p=new WrapPanel();if(HasOptions&&insideAction!=null)p.Children.Add(SnapBoard.Button(InsideLabel,insideAction));return p;}
    public void SetEditing(bool value){if(value&&!editable){originalNavigation=KeyboardNavigation.GetTabNavigation(Element);KeyboardNavigation.SetTabNavigation(Element,KeyboardNavigationMode.None);elementOpacity=Element.Opacity;}if(!value&&editable)KeyboardNavigation.SetTabNavigation(Element,originalNavigation);editable=value;if(!value)selected=false;Cursor=value?Cursors.SizeAll:null;ToolTipService.SetIsEnabled(this,value);RefreshChrome();}
    private void RefreshChrome()
    {
        chrome.Visibility=editable&&Element.ActualWidth>1&&Element.ActualHeight>1?Visibility.Visible:Visibility.Collapsed;outline.SetResourceReference(BorderBrushProperty,selected?"AccentBrush":"CardBorderBrush");outline.BorderThickness=new Thickness(selected?2:1);outline.Opacity=selected?1:.45;
        actions.Visibility=InlineActions&&selected&&!dragging?Visibility.Visible:Visibility.Collapsed;if(actions.Children.Count>1)actions.Children[0].Visibility=HasOptions?Visibility.Visible:Visibility.Collapsed;
        moveGrip.Visibility=IsContainer&&!dragging&&(scope is SnapBoard||selected||IsMouseOver)?Visibility.Visible:Visibility.Collapsed;foreach(var grip in resizeGrips)grip.Visibility=ShowResizeGrip&&sizeAction!=null&&!dragging&&scope is SnapBoard?Visibility.Visible:Visibility.Collapsed;landing.Visibility=dragging?Visibility.Visible:Visibility.Collapsed;
    }
    public void Dim(bool value){IsHitTestVisible=!value;}
    internal static Payload? Read(DragEventArgs e,object scope)=>e.Data.GetData(typeof(Payload)) is Payload p&&ReferenceEquals(p.Scope,scope)?p:null;
    internal ImageSource Snapshot(){var dpi=VisualTreeHelper.GetDpi(this);double scale=Math.Min(Math.Max(dpi.DpiScaleX,dpi.DpiScaleY),Math.Min(2400/Math.Max(1,ActualWidth),2400/Math.Max(1,ActualHeight)));var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth*scale)),Math.Max(1,(int)Math.Ceiling(ActualHeight*scale)),96*scale,96*scale,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){var destination=Element.TransformToAncestor(this).TransformBounds(new Rect(Element.RenderSize));dc.DrawRectangle(new VisualBrush(Element){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(Element.RenderSize),Stretch=Stretch.Fill},null,destination);}bitmap.Render(drawing);bitmap.Freeze();return bitmap;}
    private sealed class OverlayChrome:Panel
    {
        public bool Outer {get;set;}
        protected override Size MeasureOverride(Size available){foreach(UIElement c in Children)c.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));return new Size();}
        protected override Size ArrangeOverride(Size f){
            Children[0].Arrange(new Rect(f));Children[1].Arrange(new Rect(f));var a=Children[2].DesiredSize;
            Children[2].Arrange(new Rect(Math.Max(0,f.Width-a.Width-20),Outer?12:-9,a.Width,a.Height));Children[3].Arrange(new Rect(Outer?16:Math.Max(0,f.Width-a.Width-16),Outer?10:0,12,14));
            const double edge=10,corner=16;double w=f.Width,h=f.Height;
            Children[4].Arrange(new Rect(0,corner,Math.Min(edge,w),Math.Max(0,h-2*corner)));
            Children[5].Arrange(new Rect(Math.Max(0,w-edge),corner,Math.Min(edge,w),Math.Max(0,h-2*corner)));
            Children[6].Arrange(new Rect(corner,0,Math.Max(0,w-2*corner),Math.Min(edge,h)));
            Children[7].Arrange(new Rect(corner,Math.Max(0,h-edge),Math.Max(0,w-2*corner),Math.Min(edge,h)));
            Children[8].Arrange(new Rect(0,0,Math.Min(corner,w),Math.Min(corner,h)));
            Children[9].Arrange(new Rect(Math.Max(0,w-corner),0,Math.Min(corner,w),Math.Min(corner,h)));
            Children[10].Arrange(new Rect(0,Math.Max(0,h-corner),Math.Min(corner,w),Math.Min(corner,h)));
            Children[11].Arrange(new Rect(Math.Max(0,w-corner),Math.Max(0,h-corner),Math.Min(corner,w),Math.Min(corner,h)));return f;
        }
    }
}
