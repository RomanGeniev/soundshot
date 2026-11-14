using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GraphiteCompanion.Services;
using Forms=System.Windows.Forms;
using PointI=System.Drawing.Point;

namespace GraphiteCompanion.Controls;

public sealed class MetricsOverlay : Window
{
    private readonly MetricView view=new();
    private readonly Viewbox fit=new(){Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly};
    private readonly bool editing;
    private MonitorProfile profile=new();
    private IntPtr handle;
    private PointI? start;
    private PointI? pressedAt;
    private RectI startRect;
    private bool fitPending;
    private bool layoutPending;
    private readonly System.Windows.Threading.DispatcherTimer displayChange=new(){Interval=TimeSpan.FromMilliseconds(150)};
    public bool Accepted {get;private set;}
    public MonitorProfile Result=>profile.Copy();
    public MetricsOverlay(bool edit=false)
    {
        editing=edit;Title=edit?"Разместить метрики — Enter сохранить, Esc отменить":"SoundShot · Метрики";
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=edit;ShowActivated=edit;SizeToContent=SizeToContent.WidthAndHeight;
        fit.Child=view;fit.HorizontalAlignment=HorizontalAlignment.Left;
        view.SizeChanged+=(_,_)=>{
            if(!IsVisible||layoutPending)return;layoutPending=true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{layoutPending=false;if(!IsVisible)return;if(start!=null)fitPending=true;else FitAndPlace();}));
        };
        if(edit){
            var body=new DockPanel();
            var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Background=new SolidColorBrush(Color.FromRgb(20,26,38)),Margin=new Thickness(0,0,0,4)};
            toolbar.Children.Add(new TextBlock{Text="⠿ Переместить",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,12,0)});
            var accept=MonitorUi.Button("Готово",Accept,true);accept.Cursor=Cursors.Arrow;accept.Margin=new Thickness(0,4,4,4);toolbar.Children.Add(accept);
            var cancel=MonitorUi.Button("Отмена",Close);cancel.Cursor=Cursors.Arrow;cancel.Margin=new Thickness(0,4,4,4);toolbar.Children.Add(cancel);
            DockPanel.SetDock(toolbar,Dock.Top);body.Children.Add(toolbar);body.Children.Add(fit);
            Content=new Border{Child=body,BorderBrush=Brushes.DeepSkyBlue,BorderThickness=new Thickness(1),Background=new SolidColorBrush(Color.FromArgb(40,20,30,45))};Cursor=Cursors.SizeAll;
        }else Content=fit;
        SourceInitialized+=(_,_)=>
        {
            handle=new WindowInteropHelper(this).Handle;int style=GetWindowLong(handle,-20);if(!editing)style|=0x80;
            if(!editing)style|=0x20|0x08000000;SetWindowLong(handle,-20,style);
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{
                if(editing&&msg==0x0201){var point=new PointI(unchecked((short)(l.ToInt64()&0xffff)),unchecked((short)((l.ToInt64()>>16)&0xffff)));using var scope=OverlayDisplay.Physical();if(ClientToScreen(h,ref point))pressedAt=point;}
                if(msg is 0x007e or 0x02e0){displayChange.Stop();displayChange.Start();}if(!editing&&msg==0x84){handled=true;return new IntPtr(-1);}if(!editing&&msg==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
        };
        displayChange.Tick+=(_,_)=>{displayChange.Stop();if(!IsVisible)return;if(start!=null)fitPending=true;else FitAndPlace();};Closed+=(_,_)=>displayChange.Stop();
        PreviewMouseLeftButtonDown+=(_,e)=>{if(!editing||IsButton(e.OriginalSource as DependencyObject)){pressedAt=null;return;}GetPhysicalCursorPos(out var p);GetWindowRect(handle,out startRect);start=pressedAt??p;pressedAt=null;CaptureMouse();e.Handled=true;};
        PreviewMouseMove+=(_,e)=>{if(start is not { } p||e.LeftButton!=MouseButtonState.Pressed)return;GetPhysicalCursorPos(out var at);SetWindowPos(handle,IntPtr.Zero,startRect.Left+at.X-p.X,startRect.Top+at.Y-p.Y,0,0,0x15);};
        PreviewMouseLeftButtonUp+=(_,e)=>{if(start==null)return;start=null;ReleaseMouseCapture();Remember();if(fitPending){fitPending=false;FitAndPlace();}e.Handled=true;};
        LostMouseCapture+=(_,_)=>start=null;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}else if(e.Key==Key.Enter){e.Handled=true;Accept();}};
    }
    private static bool IsButton(DependencyObject? source){while(source!=null){if(source is ButtonBase)return true;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}return false;}
    private void Accept(){Remember();Accepted=true;Close();}
    public void Apply(MonitorProfile value)
    {
        profile=value.Normalize();view.Apply(profile);
        FitAndPlace();
    }
    private void FitAndPlace()
    {
        using var context=OverlayDisplay.Physical();
        var screen=FindScreen(profile.Monitor);var area=screen.WorkingArea;
        new WindowInteropHelper(this).EnsureHandle();SetWindowPos(handle,IntPtr.Zero,area.Left+12,area.Top+12,0,0,0x15);
        double dpi=Math.Max(96,GetDpiForWindow(handle))/96d;fit.MaxWidth=Math.Max(120,(area.Width-16)/dpi);fit.MaxHeight=Math.Max(40,(area.Height-16)/dpi-(editing?64:0));
        if(!IsVisible)Show();UpdateLayout();Place(screen);
    }
    public void Update(MetricSnapshot? snapshot,MetricHistory history)=>view.Update(snapshot,history);
    private static Forms.Screen FindScreen(string name)=>Array.Find(Forms.Screen.AllScreens,s=>s.DeviceName==name)??Forms.Screen.PrimaryScreen??Forms.Screen.AllScreens[0];
    private void Place(Forms.Screen screen)
    {
        if(start!=null)return;GetWindowRect(handle,out var rect);
        var top=fit.PointToScreen(new Point(0,0));var bottom=fit.PointToScreen(new Point(fit.ActualWidth,fit.ActualHeight));
        var area=screen.WorkingArea;var point=OverlayDisplay.Position(area,new((int)Math.Ceiling(bottom.X-top.X),(int)Math.Ceiling(bottom.Y-top.Y)),profile.X,profile.Y);
        int x=(int)Math.Round(point.X-(top.X-rect.Left)),y=(int)Math.Round(point.Y-(top.Y-rect.Top));
        x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-(rect.Right-rect.Left)));y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-(rect.Bottom-rect.Top)));
        SetWindowPos(handle,new IntPtr(-1),x,y,0,0,0x11);
    }
    private void Remember()
    {
        using var context=OverlayDisplay.Physical();
        var screen=Forms.Screen.FromHandle(handle);var a=screen.WorkingArea;profile.Monitor=screen.DeviceName;
        // Store the metric surface, excluding temporary editing buttons and borders.
        var top=fit.PointToScreen(new Point(0,0));var bottom=fit.PointToScreen(new Point(fit.ActualWidth,fit.ActualHeight));
        profile.X=Math.Clamp((top.X-a.Left)/Math.Max(1,a.Width-(bottom.X-top.X)),0,1);profile.Y=Math.Clamp((top.Y-a.Top)/Math.Max(1,a.Height-(bottom.Y-top.Y)),0,1);
    }
    [StructLayout(LayoutKind.Sequential)]private struct RectI{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLong(IntPtr h,int index,int value);
    private static bool GetWindowRect(IntPtr h,out RectI rect){using var context=OverlayDisplay.Physical();return NativeGetWindowRect(h,out rect);}
    private static bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags){using var context=OverlayDisplay.Physical();return NativeSetWindowPos(h,after,x,y,w,hgt,flags);}
    [DllImport("user32.dll",EntryPoint="GetWindowRect")]private static extern bool NativeGetWindowRect(IntPtr h,out RectI rect);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")]private static extern bool NativeSetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
    [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out PointI p);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr h,ref PointI p);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr h);
}
