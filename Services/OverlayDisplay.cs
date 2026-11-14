using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GraphiteCompanion.Services;

public static class OverlayDisplay
{
    public static (double Width,double Height) Limits(Rectangle bounds,double dpi,double preferredWidth)
    {
        double scale=Math.Max(96,double.IsFinite(dpi)?dpi:96)/96;
        return(Math.Min(Math.Max(1,preferredWidth),Math.Max(1,(bounds.Width-32)/scale)),Math.Max(1,(bounds.Height-32)/scale));
    }
    public static Point Position(Rectangle bounds,Size panel,double x,double y)
    {
        x=double.IsFinite(x)?Math.Clamp(x,0,1):.5;y=double.IsFinite(y)?Math.Clamp(y,0,1):.5;
        return new(bounds.Left+(int)Math.Round(Math.Max(0,bounds.Width-panel.Width)*x),bounds.Top+(int)Math.Round(Math.Max(0,bounds.Height-panel.Height)*y));
    }
    public static Rectangle? ForegroundBounds()
    {
        var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out var pid);
        return foreground==IntPtr.Zero||pid==Environment.ProcessId?null:Bounds(foreground);
    }
    public static Rectangle? Bounds(IntPtr window)
    {
        using var context=Physical();var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        return GetMonitorInfo(MonitorFromWindow(window,2),ref info)?Rectangle.FromLTRB(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right,info.Monitor.Bottom):null;
    }
    public static IDisposable Physical()=>new DpiScope();
    public static string ForegroundDescription(){var h=GetForegroundWindow();GetWindowThreadProcessId(h,out var pid);return $"0x{h.ToInt64():X}/pid={pid}";}
    private sealed class DpiScope:IDisposable{private readonly IntPtr before=SetThreadDpiAwarenessContext(new(-4));public void Dispose(){if(before!=IntPtr.Zero)SetThreadDpiAwarenessContext(before);}}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
