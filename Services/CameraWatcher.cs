namespace DynamicIslandNotifier.Services;

using System;
using System.Threading;
using Microsoft.Win32;

/// <summary>
/// Notices a video call: whether any app is using the camera right now (Meet in
/// the browser, Zoom, Teams, Messenger…). Windows notes this for its privacy
/// indicator under CapabilityAccessManager; an app still using the camera has a
/// start time and no stop time. Only that is read, every 3 seconds; nothing is
/// recorded and the camera is never touched.
/// </summary>
public sealed class CameraWatcher
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private Timer? _timer;
    private bool _inUse;

    /// <summary>The camera started or stopped being used. Raised on a background thread.</summary>
    public event Action<bool>? Changed;

    public bool InUse => _inUse;

    public void Start() => _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));

    private void Check()
    {
        bool now;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Store);
            now = root != null && AnyInUse(root, depth: 0);
        }
        catch { return; }
        if (now == _inUse) return;
        _inUse = now;
        CrashLog.Write(now ? "Camera in use: light mode" : "Camera off: full effects again");
        Changed?.Invoke(now);
    }

    /// <summary>Store apps sit right under the key; desktop apps (browsers, Zoom) under "NonPackaged".</summary>
    private static bool AnyInUse(RegistryKey key, int depth)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub == null) continue;
            if (sub.GetValue("LastUsedTimeStart") is long start && start != 0 &&
                sub.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                return true;
            if (depth == 0 && name == "NonPackaged" && AnyInUse(sub, depth + 1)) return true;
        }
        return false;
    }
}
