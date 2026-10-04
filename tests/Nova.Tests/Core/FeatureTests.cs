using Nova.App.Tray;
using Nova.Core.Animation;
using Nova.Core.Hotkeys;
using Nova.Core.Monitors;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Core.Tools;

namespace Nova.Tests.Core;

public class LauncherTests
{
    private readonly FakeStarter _starter = new();
    private readonly FakeFileSystem _fs = new();
    private readonly AppLauncher _launcher;

    public LauncherTests() => _launcher = new AppLauncher(_starter, _fs);

    [Fact]
    public void Executable_LaunchesWithWorkingDirectory()
    {
        _fs.Files.Add(@"C:\Apps\Tool\tool.exe");
        var result = _launcher.Launch(new QuickApp { Name = "Tool", Kind = QuickAppKind.Executable, Target = @"C:\Apps\Tool\tool.exe", Arguments = "--fast" });
        Assert.True(result.Success);
        var info = Assert.Single(_starter.Started);
        Assert.Equal(@"C:\Apps\Tool\tool.exe", info.FileName);
        Assert.Equal("--fast", info.Arguments);
        Assert.Equal(@"C:\Apps\Tool", info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void AppsFolderEntry_LaunchesThroughShell()
    {
        var result = _launcher.Launch(new QuickApp { Name = "Calc", Kind = QuickAppKind.AppsFolder, Target = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });
        Assert.True(result.Success);
        var info = _starter.Started.Single();
        Assert.Equal("explorer.exe", info.FileName);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", info.Arguments);
    }

    [Fact]
    public void MissingExecutable_FailsWithFriendlyMessage_AndDoesNotStart()
    {
        var result = _launcher.Launch(new QuickApp { Name = "Gone", Kind = QuickAppKind.Executable, Target = @"C:\Nope\gone.exe" });
        Assert.False(result.Success);
        Assert.Contains("can't be found", result.Error);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public void InvalidTargets_AreRejected()
    {
        Assert.Equal(LaunchValidation.Invalid, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Uri, Target = "not a url" }));
        Assert.Equal(LaunchValidation.Invalid, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Uri, Target = @"file:///C:/x.exe" }));
        Assert.Equal(LaunchValidation.Invalid, _launcher.Validate(new QuickApp { Kind = QuickAppKind.AppsFolder, Target = "evil\" & calc" }));
        Assert.Equal(LaunchValidation.Invalid, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Executable, Target = "" }));
        Assert.Equal(LaunchValidation.Ok, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Uri, Target = "https://github.com" }));
        Assert.Equal(LaunchValidation.Ok, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Executable, Target = "notepad.exe" }));
    }

    [Fact]
    public void Folder_RequiresExistingDirectory()
    {
        _fs.Directories.Add(@"C:\Projects");
        Assert.Equal(LaunchValidation.Ok, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Folder, Target = @"C:\Projects" }));
        Assert.Equal(LaunchValidation.Missing, _launcher.Validate(new QuickApp { Kind = QuickAppKind.Folder, Target = @"C:\Missing" }));
    }

    [Fact]
    public void StarterFailure_IsReported_AndLaunchedEventOnlyOnSuccess()
    {
        var launched = 0;
        _launcher.Launched += _ => launched++;
        _starter.FailWith = "Access denied";
        var failed = _launcher.Launch(new QuickApp { Name = "Site", Kind = QuickAppKind.Uri, Target = "https://example.com" });
        Assert.False(failed.Success);
        Assert.Equal("Access denied", failed.Error);
        Assert.Equal(0, launched);
        _starter.FailWith = null;
        Assert.True(_launcher.Launch(new QuickApp { Name = "Site", Kind = QuickAppKind.Uri, Target = "https://example.com" }).Success);
        Assert.Equal(1, launched);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("Win+Shift+N")]
    [InlineData("Ctrl+F12")]
    [InlineData("Alt+Shift+Num5")]
    public void ParseFormat_RoundTrips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(text, hk.ToString());
        Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndNormalizesOrder()
    {
        Assert.True(Hotkey.TryParse("space + alt + CONTROL", out var hk));
        Assert.Equal("Ctrl+Alt+Space", hk.ToString());
        Assert.Equal(0x20, hk.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Banana")]
    public void Parse_RejectsInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("A", HotkeyConflictLevel.Blocked)]
    [InlineData("Shift+A", HotkeyConflictLevel.Blocked)]
    [InlineData("Alt+Tab", HotkeyConflictLevel.Blocked)]
    [InlineData("Win+L", HotkeyConflictLevel.Blocked)]
    [InlineData("Ctrl+Space", HotkeyConflictLevel.Warning)]
    [InlineData("Ctrl+C", HotkeyConflictLevel.Warning)]
    [InlineData("Ctrl+Alt+Space", HotkeyConflictLevel.None)]
    [InlineData("Ctrl+Shift+F9", HotkeyConflictLevel.None)]
    public void Conflicts_AreDetected(string text, HotkeyConflictLevel expected)
    {
        Hotkey hk;
        if (text.Length == 1) hk = new Hotkey(HotkeyModifiers.None, text[0]);
        else Assert.True(Hotkey.TryParse(text, out hk));
        Assert.Equal(expected, hk.CheckConflicts().Level);
    }

    [Fact]
    public void DefaultHotkey_HasNoConflicts()
    {
        Assert.True(Hotkey.TryParse(BehaviorSettings.DefaultHotkey, out var hk));
        Assert.Equal(HotkeyConflictLevel.None, hk.CheckConflicts().Level);
    }
}

public class StartupTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\NOVA\current\NOVA.exe";
    private readonly FakeRegistry _registry = new();

    [Fact]
    public void Enable_WritesQuotedCommandWithBackgroundFlag()
    {
        var m = new StartupManager(_registry, Exe);
        Assert.True(m.SetEnabled(true));
        Assert.Equal($"\"{Exe}\" --background", _registry.Run["NOVA"]);
        Assert.Equal(StartupStatus.Enabled, m.GetStatus());
    }

    [Fact]
    public void Disable_RemovesValue()
    {
        var m = new StartupManager(_registry, Exe);
        m.SetEnabled(true);
        m.SetEnabled(false);
        Assert.False(_registry.Run.ContainsKey("NOVA"));
        Assert.Equal(StartupStatus.Disabled, m.GetStatus());
    }

    [Fact]
    public void DisabledInTaskManager_IsDetected_AndClearedWhenUserEnables()
    {
        var m = new StartupManager(_registry, Exe);
        m.SetEnabled(true);
        _registry.Approved["NOVA"] = new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(StartupStatus.DisabledByUser, m.GetStatus());
        Assert.False(m.IsEnabled);
        m.SetEnabled(true);
        Assert.Equal(StartupStatus.Enabled, m.GetStatus());
    }

    [Fact]
    public void MovedExecutable_IsRepaired()
    {
        _registry.Run["NOVA"] = "\"D:\\old\\NOVA.exe\" --background";
        var m = new StartupManager(_registry, Exe);
        Assert.Equal(StartupStatus.PathMismatch, m.GetStatus());
        m.RepairIfNeeded();
        Assert.Equal(StartupStatus.Enabled, m.GetStatus());
    }
}

public class ToolTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-3+5", 2)]
    [InlineData("10÷4", 2.5)]
    [InlineData("6×7", 42)]
    [InlineData("50%", 0.5)]
    [InlineData("7%3", 1)]
    [InlineData("sqrt(16)+abs(-2)", 6)]
    [InlineData("2*pi", 6.283185307179586)]
    [InlineData("1e3+1", 1001)]
    public void Calculator_Evaluates(string expression, double expected)
    {
        var r = ExpressionEvaluator.Evaluate(expression);
        Assert.True(r.Success, r.Error);
        Assert.Equal(expected, r.Value, 9);
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    [InlineData("2+")]
    [InlineData("foo(2)")]
    [InlineData("System.IO.File.Delete(\"x\")")]
    [InlineData("")]
    public void Calculator_RejectsInvalidInput(string expression) => Assert.False(ExpressionEvaluator.Evaluate(expression).Success);

    [Fact]
    public void Calculator_RejectsPathologicalNesting()
    {
        Assert.False(ExpressionEvaluator.Evaluate(new string('(', 200) + "1" + new string(')', 200)).Success);
    }

    [Fact]
    public void Timer_CountsDown_PausesAndCompletesOnce()
    {
        var time = new FakeTime();
        var timer = new CountdownTimer(time);
        var completed = 0;
        timer.Completed += _ => completed++;
        timer.SetDuration(TimeSpan.FromMinutes(1));
        timer.Start();
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(40, timer.Remaining.TotalSeconds, 1);
        timer.Pause();
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(40, timer.Remaining.TotalSeconds, 1);
        timer.Resume();
        timer.AddTime(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(49));
        Assert.False(timer.CheckCompleted());
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(timer.CheckCompleted());
        Assert.False(timer.CheckCompleted());
        Assert.Equal(1, completed);
        Assert.Equal(CountdownState.Completed, timer.State);
    }

    [Fact]
    public void Timer_SurvivesSleep_ByUsingWallClock()
    {
        var time = new FakeTime();
        var timer = new CountdownTimer(time);
        timer.SetDuration(TimeSpan.FromMinutes(10));
        timer.Start();
        time.Advance(TimeSpan.FromHours(1)); // laptop slept
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.True(timer.CheckCompleted());
    }

    [Fact]
    public void ClipboardHistory_Deduplicates_AndRespectsCapacity()
    {
        var h = new ClipboardHistory { Capacity = 3 };
        h.Add("a"); h.Add("b"); h.Add("a"); h.Add("c"); h.Add("d");
        Assert.Equal(new[] { "d", "c", "a" }, h.Items.Select(i => i.Text));
        h.Add("   ");
        Assert.Equal(3, h.Items.Count);
        h.Clear();
        Assert.Empty(h.Items);
    }
}

public class TrayTests
{
    private sealed class FakeTray : ITrayActions
    {
        public List<string> Calls { get; } = new();
        public bool IsNotchHidden { get; set; }
        public AnimationTheme CurrentTheme { get; set; } = AnimationTheme.Glass;
        public MonitorMode MonitorMode { get; set; } = MonitorMode.Selected;
        public string? SelectedMonitorDevice { get; set; } = @"\\.\DISPLAY2";
        public IReadOnlyList<MonitorInfo> Monitors { get; set; } = new[]
        {
            Nova.Tests.Monitors.Make(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true),
            Nova.Tests.Monitors.Make(@"\\.\DISPLAY2", 1920, 0, 1920, 1080),
        };
        public bool StartWithWindows { get; set; } = true;
        public string VersionText => "v1.0.0";
        public void ShowNotch() => Calls.Add("show");
        public void HideNotch() => Calls.Add("hide");
        public void OpenSettings() => Calls.Add("settings");
        public void SetTheme(AnimationTheme theme) => Calls.Add("theme " + theme);
        public void SetMonitorMode(MonitorMode mode) => Calls.Add("mode " + mode);
        public void SelectMonitor(string deviceName) => Calls.Add("select " + deviceName);
        public void SetStartWithWindows(bool enabled) => Calls.Add("startup " + enabled);
        public void Quit() => Calls.Add("quit");
    }

    [Fact]
    public void Menu_HasEveryRequiredEntry()
    {
        var entries = TrayMenuModel.Build(new FakeTray());
        var ids = entries.Select(e => e.Id).ToList();
        foreach (var id in new[] { "header", "show", "hide", "settings", "animation", "monitor", "startup", "quit" }) Assert.Contains(id, ids);
        var themes = entries.Single(e => e.Id == "animation").Children!;
        Assert.Equal(6, themes.Count);
        Assert.Single(themes, t => t.IsChecked);
        Assert.True(themes.Single(t => t.IsChecked).Id == "theme:Glass");
        Assert.True(entries.Single(e => e.Id == "startup").IsChecked);
        Assert.Equal("quit", entries.Last().Id);
    }

    [Fact]
    public void ShowHide_EnabledStateFollowsVisibility()
    {
        var tray = new FakeTray { IsNotchHidden = true };
        var entries = TrayMenuModel.Build(tray);
        Assert.True(entries.Single(e => e.Id == "show").IsEnabled);
        Assert.False(entries.Single(e => e.Id == "hide").IsEnabled);
    }

    [Fact]
    public void MonitorSubmenu_ListsModesAndEachMonitor_WithSelectionChecked()
    {
        var monitor = TrayMenuModel.Build(new FakeTray()).Single(e => e.Id == "monitor").Children!;
        Assert.Contains(monitor, e => e.Id == "monitor:mode:Primary");
        Assert.Contains(monitor, e => e.Id == "monitor:mode:Active");
        Assert.True(monitor.Single(e => e.Id == @"monitor:select:\\.\DISPLAY2").IsChecked);
        Assert.False(monitor.Single(e => e.Id == @"monitor:select:\\.\DISPLAY1").IsChecked);
    }

    [Theory]
    [InlineData("show", "show")]
    [InlineData("hide", "hide")]
    [InlineData("settings", "settings")]
    [InlineData("theme:Aurora", "theme Aurora")]
    [InlineData("monitor:mode:Mouse", "mode Mouse")]
    [InlineData(@"monitor:select:\\.\DISPLAY1", @"select \\.\DISPLAY1")]
    [InlineData("startup", "startup False")]
    [InlineData("quit", "quit")]
    public void Commands_InvokeTheRightAction(string id, string expected)
    {
        var tray = new FakeTray();
        Assert.True(TrayMenuModel.Execute(id, tray));
        Assert.Equal(expected, Assert.Single(tray.Calls));
    }

    [Fact]
    public void UnknownCommand_IsIgnored()
    {
        var tray = new FakeTray();
        Assert.False(TrayMenuModel.Execute("format-disk", tray));
        Assert.Empty(tray.Calls);
    }
}
