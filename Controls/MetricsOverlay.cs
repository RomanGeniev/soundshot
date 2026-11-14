using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private RectI startRect;
    private bool fitPending;
    private readonly System.Windows.Threading.DispatcherTimer displayChange=new(){Interval=TimeSpan.FromMilliseconds(150)};
    public bool Accepted {get;private set;}
    public MonitorProfile Result=>profile.Copy();
    public MetricsOverlay(bool edit=false)
    {
        editing=edit;Title=edit?"Разместить метрики — Enter сохранить, Esc отменить":"SoundShot · Метрики";
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=edit;ShowActivated=edit;SizeToContent=SizeToContent.WidthAndHeight;
        fit.Child=view;var root=new Grid();root.Children.Add(fit);
        if(edit){Cursor=Cursors.SizeAll;ToolTip="Перетащите панель. Enter — сохранить положение, Esc — отменить.";}
        Content=root;SourceInitialized+=(_,_)=>
        {
            handle=new WindowInteropHelper(this).Handle;int style=GetWindowLong(handle,-20);if(!editing)style|=0x80;
            if(!editing)style|=0x20|0x08000000;SetWindowLong(handle,-20,style);
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg is 0x007e or 0x02e0){displayChange.Stop();displayChange.Start();}if(!editing&&msg==0x84){handled=true;return new IntPtr(-1);}if(!editing&&msg==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
        };
        displayChange.Tick+=(_,_)=>{displayChange.Stop();if(!IsVisible)return;if(start!=null)fitPending=true;else FitAndPlace();};Closed+=(_,_)=>displayChange.Stop();
        PreviewMouseLeftButtonDown+=(_,e)=>{if(!editing)return;GetPhysicalCursorPos(out var p);GetWindowRect(handle,out startRect);start=p;CaptureMouse();e.Handled=true;};
        PreviewMouseMove+=(_,e)=>{if(start is not { } p||e.LeftButton!=MouseButtonState.Pressed)return;GetPhysicalCursorPos(out var at);SetWindowPos(handle,IntPtr.Zero,startRect.Left+at.X-p.X,startRect.Top+at.Y-p.Y,0,0,0x15);};
        PreviewMouseLeftButtonUp+=(_,e)=>{if(start==null)return;start=null;ReleaseMouseCapture();Remember();if(fitPending){fitPending=false;FitAndPlace();}e.Handled=true;};
        LostMouseCapture+=(_,_)=>start=null;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}else if(e.Key==Key.Enter){Remember();Accepted=true;e.Handled=true;Close();}};
    }
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
        double dpi=Math.Max(96,GetDpiForWindow(handle))/96d;fit.MaxWidth=Math.Max(120,(area.Width-16)/dpi);fit.MaxHeight=Math.Max(80,(area.Height-16)/dpi);
        if(!IsVisible)Show();UpdateLayout();Place(screen);
    }
    public void Update(MetricSnapshot? snapshot,MetricHistory history)=>view.Update(snapshot,history);
    private static Forms.Screen FindScreen(string name)=>Array.Find(Forms.Screen.AllScreens,s=>s.DeviceName==name)??Forms.Screen.PrimaryScreen??Forms.Screen.AllScreens[0];
    private void Place(Forms.Screen screen)
    {
        if(start!=null)return;GetWindowRect(handle,out var rect);var point=OverlayDisplay.Position(screen.WorkingArea,new(rect.Right-rect.Left,rect.Bottom-rect.Top),profile.X,profile.Y);
        SetWindowPos(handle,new IntPtr(-1),point.X,point.Y,0,0,0x11);
    }
    private void Remember()
    {
        using var context=OverlayDisplay.Physical();
        GetWindowRect(handle,out var r);var screen=Forms.Screen.FromHandle(handle);var a=screen.WorkingArea;profile.Monitor=screen.DeviceName;
        profile.X=Math.Clamp((r.Left-a.Left)/(double)Math.Max(1,a.Width-(r.Right-r.Left)),0,1);profile.Y=Math.Clamp((r.Top-a.Top)/(double)Math.Max(1,a.Height-(r.Bottom-r.Top)),0,1);
    }
    [StructLayout(LayoutKind.Sequential)]private struct RectI{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLong(IntPtr h,int index,int value);
    private static bool GetWindowRect(IntPtr h,out RectI rect){using var context=OverlayDisplay.Physical();return NativeGetWindowRect(h,out rect);}
    private static bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags){using var context=OverlayDisplay.Physical();return NativeSetWindowPos(h,after,x,y,w,hgt,flags);}
    [DllImport("user32.dll",EntryPoint="GetWindowRect")]private static extern bool NativeGetWindowRect(IntPtr h,out RectI rect);
    [DllImport("user32.dll",EntryPoint="SetWindowPos")]private static extern bool NativeSetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
    [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out PointI p);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(IntPtr h);
}
