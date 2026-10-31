namespace DynamicIslandNotifier.Services;

using System;
using System.Threading;
using NAudio.CoreAudioApi;

/// <summary>
/// Reports changes to the system (master) volume, so the notch can show its
/// own volume bar. Follows the default speaker/headphones when it changes.
/// </summary>
public sealed class VolumeWatcher
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private string? _deviceId;
    private DateTime _ignoreUntil;
    private Timer? _deviceTimer;

    /// <summary>Level 0..1 and muted. Raised on a background thread.</summary>
    public event Action<float, bool>? Changed;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            Attach();
            // Headphones plugged in / out changes the default device.
            _deviceTimer = new Timer(_ => CheckDevice(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            DebugLog.Note($"VolumeWatcher: {ex.Message}");
        }
    }

    private void CheckDevice()
    {
        try
        {
            var id = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            if (id != null && id != _deviceId) Attach();
        }
        catch { }
    }

    private void Attach()
    {
        try
        {
            if (_device != null)
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
                _device.Dispose();
            }

            _device   = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _deviceId = _device.ID;
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;

            // Switching devices can fire a notification by itself; don't show that.
            _ignoreUntil = DateTime.UtcNow.AddSeconds(1);
        }
        catch (Exception ex)
        {
            DebugLog.Note($"VolumeWatcher attach: {ex.Message}");
        }
    }

    private void OnVolume(AudioVolumeNotificationData data)
    {
        if (DateTime.UtcNow < _ignoreUntil) return;
        Changed?.Invoke(data.MasterVolume, data.Muted);
    }
}
