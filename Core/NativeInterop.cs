namespace DynamicIslandNotifier.Core;

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

/// <summary>
/// Centralised Win32 P/Invoke helpers for window styling, transparency,
/// and always-on-top behaviour.
/// </summary>
public static class NativeInterop
{
    // ── Constants ──────────────────────────────────────────────────────
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    // ── Imports ────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    private const int ASFW_ANY = -1;

    /// <summary>
    /// Lets the next app we start bring its window to the front. Windows only
    /// allows this for the process that received the last click (us, when the
    /// island is clicked); a real toast click gets the same permission.
    /// </summary>
    public static void AllowAnyToTakeForeground() => AllowSetForegroundWindow(ASFW_ANY);

    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insert,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint SWP_NOZORDER = 0x0004;

    /// <summary>How far below the window's top edge the pointer is, in WPF units (any display scale).</summary>
    public static double CursorBelowTop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetCursorPos(out var p) || !GetWindowRect(hwnd, out var r)) return double.MaxValue;
        double scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96.0;
        return (p.Y - r.Top) / scale;
    }

    /// <summary>
    /// Puts the window at the top centre of the main screen, worked out in real
    /// screen pixels. Going through WPF's own screen size can put it off-centre
    /// (or partly off the top) when the display scale isn't 100%, when it was
    /// changed while the app ran, or when there are screens with different scales.
    /// </summary>
    public static void PlaceTopCenter(Window window, double topDip)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return;
            double scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96.0;
            int width = (int)Math.Round(window.Width * scale);
            int screenW = info.rcMonitor.Right - info.rcMonitor.Left;
            int x = info.rcMonitor.Left + (screenW - width) / 2;
            int y = info.rcMonitor.Top + (int)Math.Round(topDip * scale);
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// Whether the mouse pointer is inside the window right now, asked from
    /// Windows directly (WPF's IsMouseOver can lag when content appears or
    /// disappears under a pointer that isn't moving).
    /// </summary>
    /// <summary>
    /// Whether the pointer is over this element's own rectangle on screen,
    /// not the whole window (whose transparent margins hold the glow).
    /// </summary>
    public static bool IsCursorOver(FrameworkElement element)
    {
        try
        {
            if (!element.IsVisible || PresentationSource.FromVisual(element) == null) return false;
            if (!GetCursorPos(out var p)) return false;
            var a = element.PointToScreen(new Point(0, 0));
            var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            return p.X >= Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X)
                && p.Y >= Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCursorOver(Window window)
    {
        try
        {
            if (!window.IsVisible || !GetCursorPos(out var p)) return false;
            var hwnd = new WindowInteropHelper(window).Handle;
            return GetWindowRect(hwnd, out var r)
                && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY     = 1;

    /// <summary>
    /// True when the active window covers the whole primary screen, taskbar
    /// included: a video in fullscreen, a game, a browser after F11.
    /// The desktop, the taskbar and this app's own windows don't count.
    /// </summary>
    public static bool IsForegroundFullscreenOnPrimary()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

            // A maximized window isn't fullscreen, even when it covers the whole screen
            // (it does when the taskbar is set to auto-hide). Real fullscreen (F11,
            // videos, games) isn't "maximized" and has no title bar.
            if (IsZoomed(hwnd)) return false;
            const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;
            if ((GetWindowLong(hwnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;

            if (!GetWindowRect(hwnd, out var r)) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (!GetMonitorInfo(monitor, ref info)) return false;
            if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

            var m = info.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }
        catch
        {
            return false;
        }
    }

    // ── Public API ────────────────────────────────────────────────────
    /// <summary>
    /// Makes the window non-activatable and hidden from Alt+Tab.
    /// </summary>
    public static void MakeOverlay(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Lets a non-activating overlay take keyboard focus for typing (true),
    /// or makes it non-activating again (false).
    /// </summary>
    public static void SetActivatable(Window window, bool activatable)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style = activatable ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    private const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>
    /// Mouse clicks pass straight through the whole window to what's below
    /// (true), or the window receives them again (false).
    /// </summary>
    public static void SetClickThrough(Window window, bool through)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        int next  = through ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (next != style) SetWindowLong(hwnd, GWL_EXSTYLE, next);
    }

    /// <summary>Whether the pointer is within <paramref name="margin"/> px (DIPs) of the element.</summary>
    public static bool IsCursorNear(FrameworkElement element, double margin)
    {
        try
        {
            if (!element.IsVisible || PresentationSource.FromVisual(element) == null) return false;
            if (!GetCursorPos(out var p)) return false;
            var a = element.PointToScreen(new Point(-margin, -margin));
            var b = element.PointToScreen(new Point(element.ActualWidth + margin, element.ActualHeight + margin));
            return p.X >= Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X)
                && p.Y >= Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The pointer's position in the element's own coordinates (DIPs), or null.</summary>
    public static Point? CursorIn(FrameworkElement element)
    {
        try
        {
            if (!element.IsVisible || PresentationSource.FromVisual(element) == null) return null;
            if (!GetCursorPos(out var p)) return null;
            return element.PointFromScreen(new Point(p.X, p.Y));
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);

    /// <summary>The taskbar (and with it the tray) is there: right after sign-in it can take a while.</summary>
    public static bool TaskbarReady() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    public const int WM_HOTKEY = 0x0312, QuickNoteHotkey = 0xB001;

    /// <summary>Ctrl + Alt + N, sent to the window as WM_HOTKEY. False if another app has it.</summary>
    public static bool RegisterQuickNoteHotkey(IntPtr hwnd) =>
        RegisterHotKey(hwnd, QuickNoteHotkey, 0x0001 /*Alt*/ | 0x0002 /*Ctrl*/ | 0x4000 /*no repeat*/, 0x4E /*N*/);

    /// <summary>Whether a key (virtual-key code) is held down right now, whichever window has focus.</summary>
    public static bool KeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>How long since the last key press or mouse move anywhere.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        uint ms = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>
    /// Forces the window to stay above all other windows including fullscreen apps.
    /// </summary>
    public static void ForceTopMost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
