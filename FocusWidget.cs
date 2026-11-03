namespace DynamicIslandNotifier;

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;
using DynamicIslandNotifier.Services;
using DynamicIslandNotifier.UI;

/// <summary>
/// A small floating card while a tree grows: the tree, the time left and a
/// progress line, in a corner of the screen. It never gets in the way: the
/// pointer passes straight through it, and it vanishes while the pointer is
/// over it. Right-click where it is for a menu (give up, move, hide).
/// </summary>
public sealed class FocusWidget : Window
{
    private readonly FocusService _focus;
    private readonly TreeShape _tree = new() { LayoutTransform = new ScaleTransform(0.34, 0.34), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI") };
    private readonly TextBlock _sub  = new() { Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 11, FontFamily = new FontFamily("Segoe UI") };
    private readonly ScaleTransform _fill = new(0, 1);
    private readonly Border _card;
    private readonly DispatcherTimer _tick  = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _hiddenUntilDone;
    private bool _underPointer;

    // Right-click detection: the window lets every click through, so we watch the mouse.
    private const int WH_MOUSE_LL = 14, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }

    private readonly HookProc _hookProc;   // kept alive while the hook is installed
    private IntPtr _hook;

    public FocusWidget(FocusService focus)
    {
        _focus = focus;
        _hookProc = OnMouse;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "DynamicIslandFocus";

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_time);
        text.Children.Add(_sub);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 16, 10) };
        var treeBox = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x24, 0x1A)), Child = _tree
        };
        row.Children.Add(treeBox);
        row.Children.Add(text);

        var green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
        var bar = new Grid { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 14, 5) };
        bar.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(1.5) });
        bar.Children.Add(new Border { Background = green, CornerRadius = new CornerRadius(1.5), RenderTransform = _fill, RenderTransformOrigin = new Point(0, 0.5) });

        var grid = new Grid();
        grid.Children.Add(row);
        grid.Children.Add(bar);

        _card = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(16), Margin = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0C, 0x0C, 0x0E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x34, 0xC7, 0x59)), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x34, 0xC7, 0x59), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.25 }
        };
        Content = _card;

        _tick.Tick  += (_, _) => Refresh();
        _hover.Tick += (_, _) => WatchPointer();
        _focus.Changed += () => Dispatcher.BeginInvoke(new Action(Sync));
        SizeChanged += (_, _) => Place();
        SourceInitialized += (_, _) =>
        {
            NativeInterop.MakeOverlay(this);
            NativeInterop.SetClickThrough(this, true);
        };
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.WorkArea)) Place(); };

        Sync();
    }

    /// <summary>Shows the card while a tree grows, hides it otherwise.</summary>
    private void Sync()
    {
        if (!_focus.IsRunning) _hiddenUntilDone = false;
        bool show = _focus.IsRunning && !_hiddenUntilDone;
        _underPointer = false;
        if (show && !IsVisible)
        {
            Refresh();
            Opacity = 0;
            Show();
            Place();
            NativeInterop.ForceTopMost(this);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            _tick.Start();
            _hover.Start();
            InstallHook();
        }
        else if (!show && IsVisible)
        {
            _tick.Stop();
            _hover.Stop();
            RemoveHook();
            Hide();
        }
    }

    private void Refresh()
    {
        if (!_focus.IsRunning) return;
        var left = _focus.Remaining;
        _time.Text = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");
        var done = TimeSpan.FromMinutes(_focus.Minutes) - left;
        _sub.Text  = $"{TreeCatalog.Get(_focus.CurrentKind).Name} · đã học {(int)done.TotalMinutes} / {_focus.Minutes} phút";
        _tree.Kind = _focus.CurrentKind;
        _tree.Set(_focus.Progress, dead: false);
        _fill.ScaleX = _focus.Progress;
    }

    /// <summary>In the corner chosen from the menu (bottom right by default), above the taskbar.</summary>
    private void Place()
    {
        if (!IsLoaded && !IsVisible) return;
        var area = SystemParameters.WorkArea;
        var corner = AppSettings.Current.FocusWidgetCorner ?? "BottomRight";
        double w = ActualWidth, h = ActualHeight;
        Left = corner.EndsWith("Left") ? area.Left + 4 : area.Right - w - 4;
        Top  = corner.StartsWith("Top") ? area.Top + 4 : area.Bottom - h - 4;
    }

    // ── Vanish under the pointer ──────────────────────────────────────
    private void WatchPointer()
    {
        bool over = NativeInterop.IsCursorOver(_card);
        if (over == _underPointer) return;
        _underPointer = over;
        BeginAnimation(OpacityProperty, new DoubleAnimation(over ? 0 : 1, TimeSpan.FromMilliseconds(over ? 70 : 220)));
    }

    // ── Right-click menu ──────────────────────────────────────────────
    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsVisible && ((int)wParam == WM_RBUTTONDOWN || (int)wParam == WM_RBUTTONUP))
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (IsOnCard(info.X, info.Y))
            {
                if ((int)wParam == WM_RBUTTONUP) Dispatcher.BeginInvoke(new Action(ShowMenu));
                return (IntPtr)1;   // the app underneath doesn't get this right-click
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool IsOnCard(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        // The card sits inside a 14 px margin (room for the glow).
        var dpi = VisualTreeHelper.GetDpi(this);
        int m = (int)(14 * dpi.DpiScaleX);
        return x >= r.Left + m && x < r.Right - m && y >= r.Top + m && y < r.Bottom - m;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };

        var giveUp = new MenuItem { Header = "Bỏ cuộc (cây sẽ chết)" };
        var sure = new MenuItem { Header = "Chắc chắn, bỏ cuộc" };
        sure.Click += (_, _) => _focus.GiveUp();
        giveUp.Items.Add(sure);
        menu.Items.Add(giveUp);

        var move = new MenuItem { Header = "Chuyển góc" };
        foreach (var (key, label) in new[] { ("TopLeft", "Trên trái"), ("TopRight", "Trên phải"), ("BottomLeft", "Dưới trái"), ("BottomRight", "Dưới phải") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = (AppSettings.Current.FocusWidgetCorner ?? "BottomRight") == key };
            item.Click += (_, _) =>
            {
                AppSettings.Current.FocusWidgetCorner = key;
                AppSettings.Current.Save();
                Place();
            };
            move.Items.Add(item);
        }
        menu.Items.Add(move);

        var hide = new MenuItem { Header = "Ẩn đến khi trồng xong" };
        hide.Click += (_, _) => { _hiddenUntilDone = true; Sync(); };
        menu.Items.Add(hide);

        menu.IsOpen = true;
    }

    public void Shutdown()
    {
        RemoveHook();
        Close();
    }
}
