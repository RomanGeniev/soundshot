using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GraphiteCompanion.Services;
using Forms = System.Windows.Forms;
using PixelPoint = System.Drawing.Point;
using PixelRect = System.Drawing.Rectangle;

namespace GraphiteCompanion.Controls;

// Independent, desktop-composed game panel. Normal clicks and dragging leave
// the foreground game active; only an explicit text-entry dialog takes focus.
public sealed class GameOverlayWindow : Window
{
    private readonly GameOverlayPlacement placement;
    private readonly Func<GameOverlayPlacement, string?> save;
    private readonly FrameworkElement gameCard;
    private readonly Border grip;
    private readonly TextBlock status = GameUi.Text("", 11, true);
    private readonly DispatcherTimer displayChange = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly double preferredWidth;
    private IntPtr hwnd;
    private HwndSource? source;
    private PixelPoint? dragOffset;
    private PixelPoint? pressedAt;
    private bool ready, placing, closed;

    public GameOverlayWindow(string game, OverlayGamesState state, GameOverlayPlacement? saved, Func<GameOverlayPlacement, string?> persist)
    {
        string name = game switch { "twentyOne" => "21", "roulette" => "Рулетка", "random" => "Случайное число", _ => throw new ArgumentException("Unknown game", nameof(game)) };
        placement = saved?.Copy() ?? new() { X = game == "twentyOne" ? .6 : game == "roulette" ? .8 : .4, Y = game == "roulette" ? .3 : .15 };
        save = persist;
        Title = "SoundShot · " + name;
        preferredWidth = game == "roulette" ? 360 : game == "twentyOne" ? 340 : 350;
        Width = preferredWidth;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        Opacity = 0; // Reveal only after WPF finishes its initial native placement.
        SizeToContent = SizeToContent.Height; UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(BackgroundProperty, "InsetBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel();
        var header = new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = GameUi.Text("⠿  " + name, 12, true); label.VerticalAlignment = VerticalAlignment.Center;
        grip = new Border { Child = label, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Padding = new Thickness(14, 10, 4, 10) };
        System.Windows.Automation.AutomationProperties.SetName(grip, "Переместить окно «" + name + "»");
        header.Children.Add(grip);
        var close = GameUi.Button("×", Close); close.Width = 32; close.Padding = new Thickness(0); close.Margin = new Thickness(4, 5, 7, 5);
        System.Windows.Automation.AutomationProperties.SetName(close, "Закрыть «" + name + "»"); Grid.SetColumn(close, 1); header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        status.Margin = new Thickness(14, 0, 14, 6); status.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        gameCard = game switch { "twentyOne" => new TwentyOneCard(state.Wallet), "roulette" => new RouletteCard(state), _ => new NumberRandomizerCard(state) };
        root.Children.Add(new ScrollViewer { Content = gameCard, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var frame = new Border { Child = root, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) }; frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush"); Content = frame;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); // NOACTIVATE, TOOLWINDOW
            source = HwndSource.FromHwnd(hwnd); source.AddHook(WindowProc);
            // Do not force software rendering: the cached wheel uses the WPF compositor.
            int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            SetLimits(FindScreen());
        };
        ContentRendered += (_, _) =>
        {
            if (ready) return;
            ready = true; PlaceSaved(); Opacity = 1;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) & ~0x00080000);
        };
        SizeChanged += (_, _) => { if (ready && !placing && dragOffset == null) QueueReclamp(); };
        displayChange.Tick += (_, _) => { displayChange.Stop(); if (dragOffset == null) Reclamp(); };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (!ReadRect(out var rect)) return;
            // Use the mouse-down message's position, not the cursor's later
            // position when WPF dispatches a queued batch of drag events.
            var pointer = pressedAt ?? Pointer(); pressedAt = null;
            dragOffset = new(pointer.X - rect.Left, pointer.Y - rect.Top);
            if (!grip.CaptureMouse()) dragOffset = null;
            e.Handled = true;
        };
        grip.MouseMove += (_, e) => { if (dragOffset != null) { MoveToPointer(); e.Handled = true; } };
        grip.MouseLeftButtonUp += (_, e) => { if (dragOffset != null) { MoveToPointer(); EndDrag(); e.Handled = true; } };
        grip.LostMouseCapture += (_, _) => EndDrag();
        Closed += (_, _) =>
        {
            closed = true; displayChange.Stop(); dragOffset = null;
            source?.RemoveHook(WindowProc);
            if (gameCard is IDisposable disposable) disposable.Dispose();
        };
    }

    public void BringForward()
    {
        var dialog = OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible);
        if (dialog != null) { dialog.Activate(); return; }
        NativePlace(0, 0, 0x0001 | 0x0002 | 0x0010); // no move/size/activation
    }
    private Forms.Screen FindScreen()
    {
        using var physical = OverlayDisplay.Physical();
        return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == placement.Monitor) ?? Forms.Screen.FromPoint(Pointer());
    }
    private void SetLimits(Forms.Screen screen)
    {
        using var physical = OverlayDisplay.Physical();
        var limits = OverlayDisplay.Limits(screen.WorkingArea, hwnd == IntPtr.Zero ? 96 : GetDpiForWindow(hwnd), preferredWidth);
        Width = limits.Width; MaxHeight = limits.Height;
    }
    private void PlaceSaved()
    {
        if (closed) return;
        placing = true;
        try
        {
            var screen = FindScreen(); SetLimits(screen); UpdateLayout();
            using var physical = OverlayDisplay.Physical();
            if (!ReadRect(out var r)) return;
            var point = OverlayDisplay.Position(screen.WorkingArea, new(r.Right-r.Left, r.Bottom-r.Top), placement.X, placement.Y);
            NativePlace(point.X, point.Y, 0x0001 | 0x0010);
        }
        finally { placing = false; }
    }
    private void QueueReclamp() { if (!closed) { displayChange.Stop(); displayChange.Start(); } }
    private void Reclamp()
    {
        if (closed || placing || !ready || !ReadRect(out var r)) return;
        placing = true;
        try
        {
            using var physical = OverlayDisplay.Physical();
            var screen = Forms.Screen.FromRectangle(PixelRect.FromLTRB(r.Left,r.Top,r.Right,r.Bottom));
            SetLimits(screen); UpdateLayout(); ReadRect(out r);
            var area = screen.WorkingArea;
            NativePlace(Math.Clamp(r.Left, area.Left, Math.Max(area.Left,area.Right-(r.Right-r.Left))), Math.Clamp(r.Top,area.Top,Math.Max(area.Top,area.Bottom-(r.Bottom-r.Top))), 0x0001 | 0x0010);
        }
        finally { placing = false; }
    }
    private void MoveToPointer()
    {
        if (dragOffset is not { } offset) return;
        var p = Pointer(); NativePlace(p.X-offset.X,p.Y-offset.Y,0x0001|0x0004|0x0010|0x0400);
    }
    private void EndDrag()
    {
        if (dragOffset == null) return;
        dragOffset = null; if (grip.IsMouseCaptured) grip.ReleaseMouseCapture();
        Reclamp();
        if (!ReadRect(out var r)) return;
        using var physical = OverlayDisplay.Physical();
        var screen = Forms.Screen.FromRectangle(PixelRect.FromLTRB(r.Left,r.Top,r.Right,r.Bottom)); var area = screen.WorkingArea;
        placement.Monitor = screen.DeviceName;
        placement.X = Math.Clamp((r.Left-area.Left)/(double)Math.Max(1,area.Width-(r.Right-r.Left)),0,1);
        placement.Y = Math.Clamp((r.Top-area.Top)/(double)Math.Max(1,area.Height-(r.Bottom-r.Top)),0,1);
        string? error = save(placement.Copy()); status.Text = error ?? ""; status.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
    }
    private IntPtr WindowProc(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x0201)
        {
            var point = new PixelPoint(unchecked((short)(l.ToInt64() & 0xffff)), unchecked((short)((l.ToInt64() >> 16) & 0xffff)));
            using var physical = OverlayDisplay.Physical();
            if (ClientToScreen(h, ref point)) pressedAt = point;
        }
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (message is 0x007E or 0x02E0) QueueReclamp();
        return IntPtr.Zero;
    }
    private bool ReadRect(out NativeRect r) { using var physical = OverlayDisplay.Physical(); return GetWindowRect(hwnd,out r); }
    private void NativePlace(int x,int y,uint flags) { using var physical = OverlayDisplay.Physical(); SetWindowPos(hwnd,new IntPtr(-1),x,y,0,0,flags); }
    private static PixelPoint Pointer() => GetPhysicalCursorPos(out var p) ? p : Forms.Cursor.Position;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h,ref PixelPoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h,out NativeRect r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
}
