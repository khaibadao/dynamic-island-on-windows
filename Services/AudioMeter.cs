namespace DynamicIslandNotifier.Services;

using System;
using NAudio.CoreAudioApi;

/// <summary>
/// How loud the speakers are right now (0..1), read from Windows' own peak
/// meter for the default output device. Used to drive the pill's sound bars.
/// Returns null when the meter can't be read, so the caller can fall back.
/// </summary>
public sealed class AudioMeter : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private DateTime _nextDeviceCheck;
    private bool _broken;

    public float? Peak()
    {
        if (_broken) return null;
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            if (_device == null || DateTime.UtcNow >= _nextDeviceCheck)
            {
                // Headphones plugged in / out change the default device.
                var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_device == null || current.ID != _device.ID) { _device?.Dispose(); _device = current; }
                else current.Dispose();
                _nextDeviceCheck = DateTime.UtcNow.AddSeconds(3);
            }
            return _device.AudioMeterInformation.MasterPeakValue;
        }
        catch (Exception ex)
        {
            _broken = true;
            CrashLog.Write($"Audio meter unavailable: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}
