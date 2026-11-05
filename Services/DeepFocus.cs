namespace DynamicIslandNotifier.Services;

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

/// <summary>
/// Deep focus: while a tree grows, notices when you switch to something on
/// your distraction list (an app like Steam, or a site like YouTube in the
/// browser, by its window title) and raises <see cref="Distracted"/>, at most
/// every 20 seconds while you stay there. It only nudges; nothing is blocked.
/// </summary>
public sealed class DeepFocus
{
    public static readonly string[] DefaultList = { "YouTube", "Facebook", "TikTok", "Instagram", "Netflix", "Steam", "Liên Quân" };

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private readonly FocusService _focus;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _current;
    private DateTime _lastNudge;

    /// <summary>The name from the list that's in front of you now.</summary>
    public event Action<string>? Distracted;

    /// <summary>You went back to work.</summary>
    public event Action? Back;

    public DeepFocus(FocusService focus)
    {
        _focus = focus;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public static bool Enabled
    {
        get => AppSettings.Current.DeepFocus;
        set { AppSettings.Current.DeepFocus = value; AppSettings.Current.Save(); }
    }

    private void Check()
    {
        if (!Enabled || !_focus.IsRunning || !AppSettings.Current.ReadWindowTitles) { _current = null; return; }

        string? hit = Match();
        if (hit == null)
        {
            if (_current != null) { _current = null; Back?.Invoke(); }
            return;
        }
        bool fresh = hit != _current;
        _current = hit;
        if (fresh || (DateTime.UtcNow - _lastNudge).TotalSeconds >= 20)
        {
            _lastNudge = DateTime.UtcNow;
            Distracted?.Invoke(hit);
        }
    }

    /// <summary>Which item of the list the window in front matches, if any.</summary>
    private static string? Match()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return null;
            string process = "";
            try { using var p = Process.GetProcessById((int)pid); process = p.ProcessName; } catch { }

            string t = title.ToString();
            foreach (var item in AppSettings.Current.DeepFocusList.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                if (t.Contains(item, StringComparison.CurrentCultureIgnoreCase) ||
                    process.Equals(item.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }
        catch { }
        return null;
    }
}
