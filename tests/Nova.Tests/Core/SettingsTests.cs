using Nova.Core.QuickApps;
using Nova.Core.Settings;

namespace Nova.Tests.Core;

public class SettingsTests
{
    [Fact]
    public void Load_WithoutFile_CreatesDefaults()
    {
        using var dir = new TempDirectory();
        var result = new SettingsStore(dir.Path).Load();
        Assert.Equal(SettingsLoadOutcome.CreatedDefaults, result.Outcome);
        Assert.Equal(AnimationTheme.Liquid, result.Settings.Animation.Theme);
        Assert.Equal("Ctrl+Alt+Space", result.Settings.Behavior.Hotkey);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverySection()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        var s = new AppSettings
        {
            FirstRunCompleted = true,
            Monitor = { Mode = MonitorMode.Selected, SelectedDeviceId = "id-2", SelectedFriendlyName = "DELL", SelectedDeviceName = @"\\.\DISPLAY2" },
            Appearance = { Material = NotchMaterial.Frost, AccentColor = "#FF00AA", SizeScale = 1.2, Shape = NotchShape.Floating },
            Animation = { Theme = AnimationTheme.Aurora, Speed = 1.5, ReduceMotion = true },
            Media = { Spotify = false, AutoExpandDurationMs = 4000 },
            Behavior = { Hotkey = "Win+Alt+N", AutoHideMode = AutoHideMode.WhenWindowAtTop },
            Notifications = { ClipboardCopied = true },
            Modules = { Calculator = false },
            Advanced = { DebugMode = true },
        };
        s.QuickApps.Apps.Add(new QuickApp { Name = "Code", Kind = QuickAppKind.AppsFolder, Target = "Microsoft.VisualStudioCode", Hotkey = "Ctrl+Alt+C" });
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal(SettingsLoadOutcome.Loaded, loaded.Outcome);
        var l = loaded.Settings;
        Assert.True(l.FirstRunCompleted);
        Assert.Equal(MonitorMode.Selected, l.Monitor.Mode);
        Assert.Equal("id-2", l.Monitor.SelectedDeviceId);
        Assert.Equal(NotchMaterial.Frost, l.Appearance.Material);
        Assert.Equal(NotchShape.Floating, l.Appearance.Shape);
        Assert.Equal("#FF00AA", l.Appearance.AccentColor);
        Assert.Equal(AnimationTheme.Aurora, l.Animation.Theme);
        Assert.True(l.Animation.ReduceMotion);
        Assert.False(l.Media.Spotify);
        Assert.Equal("Win+Alt+N", l.Behavior.Hotkey);
        Assert.Equal(AutoHideMode.WhenWindowAtTop, l.Behavior.AutoHideMode);
        Assert.True(l.Notifications.ClipboardCopied);
        Assert.False(l.Modules.Calculator);
        Assert.True(l.Advanced.DebugMode);
        var app = Assert.Single(l.QuickApps.Apps);
        Assert.Equal("Ctrl+Alt+C", app.Hotkey);
        Assert.Equal(QuickAppKind.AppsFolder, app.Kind);
    }

    [Fact]
    public void Save_KeepsPreviousVersionAsBackup()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        store.Save(new AppSettings { Animation = { Theme = AnimationTheme.Glass } });
        store.Save(new AppSettings { Animation = { Theme = AnimationTheme.Morph } });
        Assert.True(File.Exists(store.BackupPath));
        Assert.Contains("Glass", File.ReadAllText(store.BackupPath));
        Assert.Contains("Morph", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void CorruptFile_IsQuarantined_AndBackupRestored()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        store.Save(new AppSettings { Animation = { Theme = AnimationTheme.Elastic } });
        store.Save(new AppSettings { Animation = { Theme = AnimationTheme.Elastic } });
        File.WriteAllText(store.FilePath, "{ this is not json");

        var result = store.Load();
        Assert.Equal(SettingsLoadOutcome.RecoveredFromBackup, result.Outcome);
        Assert.Equal(AnimationTheme.Elastic, result.Settings.Animation.Theme);
        Assert.NotNull(result.QuarantinedFile);
        Assert.True(File.Exists(result.QuarantinedFile));
    }

    [Fact]
    public void CorruptFile_WithoutBackup_ResetsToDefaults()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, "\0\0garbage");
        var result = store.Load();
        Assert.Equal(SettingsLoadOutcome.CorruptedReset, result.Outcome);
        Assert.Equal(AnimationTheme.Liquid, result.Settings.Animation.Theme);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues_AndRepairsMissingSections()
    {
        var s = new AppSettings
        {
            Appearance = { Opacity = 5, SizeScale = 0.1, AccentColor = "not a color" },
            Animation = { Speed = 99, ExpansionDurationMs = 1 },
            Modules = { SearchUrlTemplate = "https://example.com" },
        };
        s.Media = null!;
        s.QuickApps.Apps.Add(new QuickApp { Target = "" });
        s.Normalize();
        Assert.Equal(1.0, s.Appearance.Opacity);
        Assert.Equal(0.8, s.Appearance.SizeScale);
        Assert.Equal(AppearanceSettings.DefaultAccent, s.Appearance.AccentColor);
        Assert.Equal(2.0, s.Animation.Speed);
        Assert.Equal(150, s.Animation.ExpansionDurationMs);
        Assert.Equal(ModuleSettings.DefaultSearchUrl, s.Modules.SearchUrlTemplate);
        Assert.NotNull(s.Media);
        Assert.Empty(s.QuickApps.Apps);
    }

    [Fact]
    public void UnknownProperties_AndCommentsInJson_AreTolerated()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, """
            {
              // written by a future version
              "SchemaVersion": 0,
              "FutureThing": { "x": 1 },
              "Animation": { "Theme": "Morph", },
            }
            """);
        var result = store.Load();
        Assert.Equal(SettingsLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(AnimationTheme.Morph, result.Settings.Animation.Theme);
        Assert.Equal(AppSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
    }

    [Fact]
    public void LegacyAutoHideSwitch_MigratesToAlwaysMode_AndIsNoLongerWritten()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 1, "Behavior": { "AutoHide": true } }""");
        var result = store.Load();
        Assert.Equal(AutoHideMode.Always, result.Settings.Behavior.AutoHideMode);
        store.Save(result.Settings);
        Assert.DoesNotContain("\"AutoHide\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void SettingsService_RaisesChanged_AndPersists()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);
        SettingsSection? raised = null;
        using (var service = new SettingsService(store))
        {
            service.Changed += s => raised = s;
            service.Update(s => s.Animation.Theme = AnimationTheme.Glass, SettingsSection.Animation);
        }
        Assert.Equal(SettingsSection.Animation, raised);
        Assert.Equal(AnimationTheme.Glass, new SettingsStore(dir.Path).Load().Settings.Animation.Theme);
    }

    [Fact]
    public void ResetToDefaults_CanKeepQuickApps()
    {
        using var dir = new TempDirectory();
        using var service = new SettingsService(new SettingsStore(dir.Path));
        service.Current.QuickApps.Apps.Add(new QuickApp { Name = "A", Target = "a.exe" });
        service.Current.Animation.Theme = AnimationTheme.Aurora;
        service.ResetToDefaults(keepQuickApps: true);
        Assert.Equal(AnimationTheme.Liquid, service.Current.Animation.Theme);
        Assert.Single(service.Current.QuickApps.Apps);
    }
}
