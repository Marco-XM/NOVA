using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Nova.Core.Logging;

namespace Nova.Platform.Services;

/// <summary>
/// Listens for master-volume changes on the default playback device via the CoreAudio callback
/// (event driven, no polling) and follows default-device switches (e.g. plugging headphones).
/// </summary>
public sealed class VolumeWatcher : IDisposable, IMMNotificationClient
{
    private readonly object _gate = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private DateTime _ignoreUntil;
    private bool _disposed;

    /// <summary>Raised on a CoreAudio thread with (level 0..1, muted).</summary>
    public event Action<double, bool>? VolumeChanged;

    public bool IsAvailable { get; private set; }
    public double Level { get; private set; }
    public bool Muted { get; private set; }

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            Bind();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Log.Warn("CoreAudio unavailable; volume indicator disabled", ex);
        }
    }

    private void Bind()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is null) return;
            Unbind();
            try
            {
                if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    IsAvailable = false;
                    return;
                }
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Level = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
                Muted = _device.AudioEndpointVolume.Mute;
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                // Binding to a new device can fire a burst of notifications; don't show those.
                _ignoreUntil = DateTime.UtcNow.AddMilliseconds(800);
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Log.Warn("Binding to the default audio device failed", ex);
            }
        }
    }

    private void Unbind()
    {
        if (_device is null) return;
        try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
        try { _device.Dispose(); } catch { }
        _device = null;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        var changed = Math.Abs(data.MasterVolume - Level) > 0.001 || data.Muted != Muted;
        Level = data.MasterVolume;
        Muted = data.Muted;
        if (!changed || DateTime.UtcNow < _ignoreUntil) return;
        VolumeChanged?.Invoke(Level, Muted);
    }

    public bool SetLevel(double level)
    {
        try
        {
            lock (_gate)
            {
                if (_device is null) return false;
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(level, 0, 1);
                return true;
            }
        }
        catch (Exception ex) { Log.Warn("Setting master volume failed", ex); return false; }
    }

    // IMMNotificationClient — called on CoreAudio threads.
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) ThreadPool.QueueUserWorkItem(_ => Bind());
    }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Unbind();
            try { _enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }
}
