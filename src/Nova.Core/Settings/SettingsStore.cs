using System.Text.Json;
using System.Text.Json.Serialization;
using Nova.Core.Logging;

namespace Nova.Core.Settings;

public enum SettingsLoadOutcome
{
    Loaded,
    CreatedDefaults,
    RecoveredFromBackup,
    CorruptedReset,
}

public readonly record struct SettingsLoadResult(AppSettings Settings, SettingsLoadOutcome Outcome, string? QuarantinedFile);

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON. Writes are atomic (temp file + replace) and keep a
/// <c>.bak</c> copy, so a crash mid-write or a hand-edited broken file never loses the configuration.
/// </summary>
public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _ioLock = new();

    public SettingsStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, "settings.json");
    public string BackupPath => FilePath + ".bak";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NOVA");

    public SettingsLoadResult Load()
    {
        lock (_ioLock)
        {
            System.IO.Directory.CreateDirectory(Directory);

            if (!File.Exists(FilePath))
            {
                if (TryRead(BackupPath, out var fromBackupOnly))
                    return new(fromBackupOnly!, SettingsLoadOutcome.RecoveredFromBackup, null);
                return new(new AppSettings().Normalize(), SettingsLoadOutcome.CreatedDefaults, null);
            }

            if (TryRead(FilePath, out var settings))
                return new(settings!, SettingsLoadOutcome.Loaded, null);

            // Main file is unreadable: move it aside so the user (or support) can inspect it.
            var quarantine = Path.Combine(Directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(FilePath, quarantine, overwrite: true); }
            catch (Exception ex) { Log.Warn("Could not quarantine corrupt settings file", ex); quarantine = FilePath; }

            if (TryRead(BackupPath, out var backup))
            {
                Log.Warn($"Settings file was corrupt; restored from backup. Corrupt copy: {quarantine}");
                return new(backup!, SettingsLoadOutcome.RecoveredFromBackup, quarantine);
            }

            Log.Warn($"Settings file was corrupt and no backup existed; using defaults. Corrupt copy: {quarantine}");
            return new(new AppSettings().Normalize(), SettingsLoadOutcome.CorruptedReset, quarantine);
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        lock (_ioLock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
            {
                // File.Replace keeps the previous version as the backup atomically.
                File.Replace(temp, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
    }

    private static bool TryRead(string path, out AppSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null) return false;
            settings = Migrate(settings).Normalize();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn($"Failed to read settings from {path}", ex);
            settings = null;
            return false;
        }
    }

    /// <summary>Upgrades older schema versions in place.</summary>
    internal static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion < 1)
        {
            // Pre-release builds had no schema version; nothing structural changed.
            settings.SchemaVersion = 1;
        }
        if (settings.Behavior is { AutoHide: true } behavior)
        {
            // The auto-hide switch became a mode; "on" meant always.
            if (behavior.AutoHideMode == AutoHideMode.Off) behavior.AutoHideMode = AutoHideMode.Always;
            behavior.AutoHide = false;
        }
        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            Log.Warn($"Settings were written by a newer NOVA (schema {settings.SchemaVersion}); unknown values are ignored.");
        }
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }
}
