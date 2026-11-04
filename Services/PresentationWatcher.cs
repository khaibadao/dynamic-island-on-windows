namespace DynamicIslandNotifier.Services;

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

/// <summary>
/// Notices when you're presenting: a PowerPoint slide show, Windows'
/// presentation mode, or sharing your screen in a browser call (Meet…),
/// Zoom or Teams. The notch then steps aside and notifications wait.
/// Best effort: it goes by window names, which apps may change.
/// </summary>
public sealed class PresentationWatcher
{
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    private const int QUNS_PRESENTATION_MODE = 4;

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

    // Window classes / titles that only exist while presenting or sharing
    private static readonly string[] Classes = { "screenClass", "ZPFloatToolbarClass" };   // PowerPoint slide show, Zoom share toolbar
    private static readonly string[] Titles =
    {
        "is sharing your screen", "is sharing a window", "is sharing this tab", "đang chia sẻ màn hình", "đang chia sẻ một cửa sổ",
        "đang chia sẻ thẻ này", "Sharing control bar", "Thanh điều khiển chia sẻ", "PowerPoint Slide Show", "Trình chiếu PowerPoint"
    };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public bool IsPresenting { get; private set; }
    public event Action<bool>? Changed;

    public PresentationWatcher()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        bool now = Detect();
        if (now == IsPresenting) return;
        IsPresenting = now;
        CrashLog.Write(now ? "Presenting: notch hidden, notifications held" : "Done presenting");
        Changed?.Invoke(now);
    }

    private static bool Detect()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) == 0 && state == QUNS_PRESENTATION_MODE) return true;
            bool found = false;
            var cls = new StringBuilder(128);
            var title = new StringBuilder(256);
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;
                cls.Clear(); title.Clear();
                GetClassName(h, cls, cls.Capacity);
                GetWindowText(h, title, title.Capacity);
                string c = cls.ToString(), t = title.ToString();
                foreach (var k in Classes) if (c == k) { found = true; return false; }
                foreach (var k in Titles) if (t.Contains(k, StringComparison.OrdinalIgnoreCase)) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        catch
        {
            return false;
        }
    }
}
