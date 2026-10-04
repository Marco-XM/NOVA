using System.Text.Json;
using Nova.Core.Logging;

namespace Nova.Core.Settings;

[Flags]
public enum SettingsSection
{
    None = 0,
    Monitor = 1,
    Appearance = 2,
    Animation = 4,
    Media = 8,
    QuickApps = 16,
    Behavior = 32,
    Notifications = 64,
    Modules = 128,
    Advanced = 256,
    All = Monitor | Appearance | Animation | Media | QuickApps | Behavior | Notifications | Modules | Advanced,
}

/// <summary>
/// Owns the live <see cref="AppSettings"/> instance. Consumers mutate it and call
/// <see cref="Update"/>, which notifies listeners immediately and persists after a short debounce
/// so dragging a slider doesn't hammer the disk.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Timer _saveTimer;
    private readonly object _gate = new();
    private bool _dirty;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        var result = store.Load();
        Current = result.Settings;
        LoadOutcome = result.Outcome;
        QuarantinedFile = result.QuarantinedFile;
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        if (result.Outcome != SettingsLoadOutcome.Loaded) SaveNow(force: true);
    }

    public AppSettings Current { get; private set; }
    public SettingsLoadOutcome LoadOutcome { get; }
    public string? QuarantinedFile { get; }
    public string FilePath => _store.FilePath;

    /// <summary>Raised on the thread that called <see cref="Update"/>.</summary>
    public event Action<SettingsSection>? Changed;

    public void Update(SettingsSection section)
    {
        Current.Normalize();
        lock (_gate) _dirty = true;
        _saveTimer.Change(400, Timeout.Infinite);
        Changed?.Invoke(section);
    }

    public void Update(Action<AppSettings> mutate, SettingsSection section)
    {
        mutate(Current);
        Update(section);
    }

    public void ResetToDefaults(bool keepQuickApps = false)
    {
        var apps = Current.QuickApps;
        var firstRun = Current.FirstRunCompleted;
        Current = new AppSettings { FirstRunCompleted = firstRun }.Normalize();
        if (keepQuickApps) Current.QuickApps = apps;
        Update(SettingsSection.All);
    }

    public AppSettings Snapshot() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Current, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;

    public void SaveNow(bool force = false)
    {
        lock (_gate)
        {
            if (!_dirty && !force) return;
            _dirty = false;
        }
        try { _store.Save(Current); }
        catch (InvalidOperationException)
        {
            // A collection was modified on the UI thread while serializing; try again shortly.
            lock (_gate) _dirty = true;
            _saveTimer.Change(250, Timeout.Infinite);
        }
        catch (Exception ex) { Log.Error("Saving settings failed", ex); }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        SaveNow();
    }
}
