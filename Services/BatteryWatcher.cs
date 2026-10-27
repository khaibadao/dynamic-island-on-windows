namespace DynamicIslandNotifier.Services;

using System;
using DynamicIslandNotifier.Core;
using Windows.System.Power;

/// <summary>
/// Shows the island when the charger is plugged in or out, and warns once
/// when the battery drops to 20% and again at 10%. Does nothing on a PC
/// without a battery.
/// </summary>
public sealed class BatteryWatcher
{
    public const string SourceKey = "system:battery";

    private static readonly int[] LowLevels = { 20, 10 };

    private bool? _charging;
    private int _lowestWarned = 101;   // lowest level we've already warned about

    /// <summary>Raised on a background thread.</summary>
    public event Action<NotificationData>? Notify;

    public void Start()
    {
        try
        {
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return;

            _charging = IsCharging();
            PowerManager.PowerSupplyStatusChanged      += (_, _) => OnPowerChanged();
            PowerManager.RemainingChargePercentChanged += (_, _) => OnPercentChanged();
        }
        catch (Exception ex)
        {
            DebugLog.Note($"BatteryWatcher: {ex.Message}");
        }
    }

    private static bool IsCharging() => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;

    private void OnPowerChanged()
    {
        bool charging = IsCharging();
        if (charging == _charging) return;
        _charging = charging;

        int p = PowerManager.RemainingChargePercent;
        if (charging) _lowestWarned = 101;   // warn again after the next unplug

        Notify?.Invoke(Make(
            title: charging ? "Đang sạc pin" : "Đã rút sạc",
            body:  charging ? $"{p}%" : $"{p}%{RemainingText()}",
            charging,
            color: charging ? "#30D158" : (p <= 20 ? "#FF453A" : "#3A3A3C"),
            p));
    }

    private void OnPercentChanged()
    {
        if (_charging == true) return;
        int p = PowerManager.RemainingChargePercent;

        foreach (var level in LowLevels)
        {
            if (p <= level && level < _lowestWarned)
            {
                _lowestWarned = level;
                Notify?.Invoke(Make(
                    title: "Pin yếu",
                    body:  $"Còn {p}%{RemainingText()} · hãy cắm sạc",
                    charging: false,
                    color: "#FF453A",
                    p));
                return;
            }
        }
    }

    /// <summary>" · còn khoảng 2 giờ 15 phút" while on battery, if Windows knows.</summary>
    private static string RemainingText()
    {
        try
        {
            var t = PowerManager.RemainingDischargeTime;
            if (t <= TimeSpan.Zero || t > TimeSpan.FromDays(1)) return "";
            return t.TotalHours >= 1
                ? $" · còn khoảng {(int)t.TotalHours} giờ {t.Minutes} phút"
                : $" · còn khoảng {t.Minutes} phút";
        }
        catch { return ""; }
    }

    private static NotificationData Make(string title, string body, bool charging, string color, int percent)
    {
        // Segoe Fluent Icons: Battery0..10 = U+EBA0..EBAA, BatteryCharging0..10 = U+EBAB..EBB5
        int step  = Math.Clamp(percent / 10, 0, 10);
        int glyph = (charging ? 0xEBAB : 0xEBA0) + step;

        return new NotificationData
        {
            AppName    = "Pin",
            Title      = title,
            Body       = body,
            Glyph      = char.ConvertFromUtf32(glyph),
            GlyphColor = color,
            SourceKey  = SourceKey,
            SiteUrl    = "ms-settings:batterysaver"   // click opens Windows' battery settings
        };
    }
}
