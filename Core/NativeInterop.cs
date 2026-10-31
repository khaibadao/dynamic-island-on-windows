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
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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
    /// Forces the window to stay above all other windows including fullscreen apps.
    /// </summary>
    public static void ForceTopMost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
