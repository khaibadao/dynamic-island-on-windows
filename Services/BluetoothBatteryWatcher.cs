namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicIslandNotifier.Core;
using Windows.Devices.Enumeration;

/// <summary>
/// Battery level of connected Bluetooth headphones, as Windows reports it
/// (Settings → Bluetooth & devices). Not every headset reports its battery
/// to Windows (AirPods usually don't); those are simply not shown.
/// </summary>
public sealed class BluetoothBatteryWatcher
{
    public const string SourceKey = "system:bluetooth";

    // DEVPKEY_Bluetooth_Battery: the battery percentage Windows shows for the device.
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public sealed record Device(string Name, int Percent);

    private readonly Dictionary<string, int> _known = new();       // name → last percent
    private readonly HashSet<string> _warnedLow = new();
    private Timer? _timer;
    private int _busy;
    private bool _initialised;   // the first look only records what's already connected

    /// <summary>Connected devices with a battery level. Read from the UI thread after Changed.</summary>
    public IReadOnlyList<Device> Devices { get; private set; } = Array.Empty<Device>();

    /// <summary>The device list changed. Raised on a background thread.</summary>
    public event Action? Changed;

    /// <summary>Something worth showing on the island (connected, low battery). Background thread.</summary>
    public event Action<NotificationData>? Notify;

    public void Start()
    {
        _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));
    }

    private async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var found = await FindAsync();
            bool first = !_initialised;
            _initialised = true;

            foreach (var d in found)
            {
                if (!_known.ContainsKey(d.Name) && !first)
                {
                    Notify?.Invoke(Make($"{d.Name} đã kết nối", $"Pin {d.Percent}%", d.Percent));
                }
                else if (d.Percent <= 20 && _warnedLow.Add(d.Name))
                {
                    Notify?.Invoke(Make($"Pin {d.Name} yếu", $"Còn {d.Percent}% · nhớ sạc nhé", d.Percent, low: true));
                }
                if (d.Percent > 30) _warnedLow.Remove(d.Name);
            }

            bool changed = found.Count != Devices.Count
                           || found.Any(d => !_known.TryGetValue(d.Name, out var p) || p != d.Percent);

            _known.Clear();
            foreach (var d in found) _known[d.Name] = d.Percent;
            Devices = found;
            if (changed) Changed?.Invoke();
        }
        catch (Exception ex)
        {
            DebugLog.Note($"BluetoothBattery: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task<List<Device>> FindAsync()
    {
        // Device nodes that are present right now and report a battery level.
        var nodes = await DeviceInformation.FindAllAsync(
            "System.Devices.Present:=System.StructuredQueryType.Boolean#True",
            new[] { BatteryKey },
            DeviceInformationKind.Device);

        var result = new Dictionary<string, Device>();
        foreach (var node in nodes)
        {
            if (!node.Properties.TryGetValue(BatteryKey, out var value) || value == null) continue;

            int percent;
            try { percent = Convert.ToInt32(value); }
            catch { continue; }
            if (percent < 0 || percent > 100) continue;

            // One headset shows up as several nodes (hands-free, audio…): keep one per name.
            var name = string.IsNullOrWhiteSpace(node.Name) ? "Tai nghe" : node.Name.Trim();
            result[name] = new Device(name, percent);
        }
        return result.Values.OrderBy(d => d.Name).ToList();
    }

    private static NotificationData Make(string title, string body, int percent, bool low = false) => new()
    {
        AppName    = "Bluetooth",
        Title      = title,
        Body       = body,
        Glyph      = "",                                   // headphones
        GlyphColor = low || percent <= 20 ? "#FF453A" : "#0A84FF",
        SourceKey  = SourceKey,
        SiteUrl    = "ms-settings:bluetooth"                     // click opens Bluetooth settings
    };
}
