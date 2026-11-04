namespace DynamicIslandNotifier.Services;

using System;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

/// <summary>
/// Listens to what the speakers are playing (Windows loopback, nothing is
/// recorded or saved) and splits it into 7 bands from bass to treble, each
/// 0..1 and scaled to how loud that band has been over the last few seconds,
/// so quiet and loud songs both move the bars. Drums give a short kick.
/// </summary>
public sealed class AudioSpectrum : IDisposable
{
    public const int BandCount = 7;
    private const int FftSize = 1024, FftLog = 10;
    private static readonly double[] Edges = { 30, 90, 220, 500, 1100, 2500, 5500, 14000 };   // Hz

    private readonly object _lock = new();
    private readonly float[] _ring = new float[FftSize];
    private int _ringPos;
    private long _received;
    private int _sampleRate = 48000;

    private WasapiLoopbackCapture? _capture;
    private string? _deviceId;
    private DateTime _nextDeviceCheck;
    private bool _broken;

    private readonly double[] _max = new double[BandCount];
    private readonly double[] _avg = new double[BandCount];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _window = new float[FftSize];

    public AudioSpectrum()
    {
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));   // Hann
        for (int b = 0; b < BandCount; b++) _max[b] = 1e-4;
    }

    public bool Running => _capture != null;

    public bool Start()
    {
        if (_broken) return false;
        if (_capture != null && DateTime.UtcNow < _nextDeviceCheck) return true;
        try
        {
            using var en = new MMDeviceEnumerator();
            var device = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _nextDeviceCheck = DateTime.UtcNow.AddSeconds(3);
            if (_capture != null && device.ID == _deviceId) { device.Dispose(); return true; }   // same speakers

            Stop();
            _deviceId = device.ID;
            var capture = new WasapiLoopbackCapture(device);
            _sampleRate = capture.WaveFormat.SampleRate;
            int channels = capture.WaveFormat.Channels, bits = capture.WaveFormat.BitsPerSample;
            capture.DataAvailable += (_, e) => Feed(e.Buffer, e.BytesRecorded, channels, bits);
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) CrashLog.Write($"Sound bars: capture stopped ({e.Exception.Message})");
                if (ReferenceEquals(_capture, capture)) { _capture = null; _deviceId = null; }
            };
            capture.StartRecording();
            _capture = capture;
            return true;
        }
        catch (Exception ex)
        {
            _broken = true;
            CrashLog.Write($"Sound bars: loopback unavailable ({ex.Message})");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        _deviceId = null;
        if (c == null) return;
        try { c.StopRecording(); c.Dispose(); } catch { }
    }

    /// <summary>Mixes to mono and keeps the newest <see cref="FftSize"/> samples. Runs on the audio thread.</summary>
    private void Feed(byte[] buffer, int bytes, int channels, int bits)
    {
        int frame = channels * bits / 8;
        if (frame == 0) return;
        lock (_lock)
        {
            for (int pos = 0; pos + frame <= bytes; pos += frame)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int at = pos + ch * bits / 8;
                    sum += bits == 32 ? BitConverter.ToSingle(buffer, at)
                         : bits == 16 ? BitConverter.ToInt16(buffer, at) / 32768f
                         : 0;
                }
                _ring[_ringPos] = sum / channels;
                _ringPos = (_ringPos + 1) % FftSize;
            }
            _received += bytes / frame;
        }
    }

    private long _lastReceived;

    /// <summary>Fills <paramref name="bands"/> with 0..1 per band. False when nothing is playing.</summary>
    public bool Read(double[] bands)
    {
        if (_capture == null) return false;
        lock (_lock)
        {
            // Windows sends nothing while everything is silent
            if (_received == _lastReceived) { Array.Clear(bands); return false; }
            _lastReceived = _received;
            for (int i = 0; i < FftSize; i++)
            {
                _fft[i].X = _ring[(_ringPos + i) % FftSize] * _window[i];
                _fft[i].Y = 0;
            }
        }
        FastFourierTransform.FFT(true, FftLog, _fft);

        double hzPerBin = (double)_sampleRate / FftSize;
        bool any = false;
        for (int b = 0; b < BandCount; b++)
        {
            int from = Math.Max(1, (int)(Edges[b] / hzPerBin));
            int to = Math.Min(FftSize / 2 - 1, Math.Max(from, (int)(Edges[b + 1] / hzPerBin)));
            double energy = 0;
            for (int k = from; k <= to; k++)
                energy += _fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y;
            double v = Math.Sqrt(energy / (to - from + 1));

            // Each band is measured against its own recent loudest (fades over ~4 s)
            _max[b] = Math.Max(v, Math.Max(1e-5, _max[b] * 0.992));
            double level = v / _max[b];

            // A sudden jump above the band's usual level (a drum hit) kicks a bit higher
            _avg[b] = _avg[b] * 0.9 + level * 0.1;
            if (level > _avg[b] * 1.35 && level > 0.35) level = Math.Min(1, level * 1.25);

            // Pull the quiet part down so there's room to jump
            bands[b] = Math.Clamp((level - 0.25) / 0.75, 0, 1);
            if (v > 1e-5) any = true;
        }
        return any;
    }

    public void Dispose() => Stop();
}
