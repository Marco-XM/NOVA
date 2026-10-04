using Nova.Core.Animation;
using Nova.Core.Events;
using Nova.Core.Settings;
using Nova.Core.State;

namespace Nova.App.Infrastructure;

/// <summary>
/// Developer commands, sent with <c>NOVA.exe --debug &lt;command&gt; [args]</c> to the running instance.
/// Used for automated visual verification (driving the notch into each state) and support.
/// They only drive the UI; nothing here fakes data.
/// </summary>
public static class DebugCommands
{
    public static void Execute(AppHost host, string command, string args)
    {
        var s = host.Services;
        var sm = s.StateMachine;
        switch (command.ToLowerInvariant())
        {
            case "state":
                switch (args.Trim().ToLowerInvariant())
                {
                    case "idle": sm.SetUserHidden(false); sm.Collapse(); break;
                    case "hover": sm.PointerEntered(); sm.Tick(); break;
                    case "unhover": sm.PointerExited(); break;
                    case "expanded": case "home": sm.Navigate(NotchState.Expanded); break;
                    case "media": sm.Navigate(NotchState.Media); break;
                    case "quickapps": sm.Navigate(NotchState.QuickApps); break;
                    case "timer": sm.Navigate(NotchState.Tool, NotchTool.Timer); break;
                    case "calculator": sm.Navigate(NotchState.Tool, NotchTool.Calculator); break;
                    case "clipboard": sm.Navigate(NotchState.Tool, NotchTool.Clipboard); break;
                    case "search": sm.Navigate(NotchState.Tool, NotchTool.Search); break;
                    case "hidden": sm.SetUserHidden(true); break;
                    case "shown": sm.SetUserHidden(false); break;
                }
                break;
            case "theme":
                if (Enum.TryParse<AnimationTheme>(args.Trim(), true, out var theme)) host.SetTheme(theme);
                break;
            case "material":
                if (Enum.TryParse<NotchMaterial>(args.Trim(), true, out var material))
                    s.Settings.Update(x => x.Appearance.Material = material, SettingsSection.Appearance);
                break;
            case "shape":
                if (Enum.TryParse<NotchShape>(args.Trim(), true, out var shape))
                    s.Settings.Update(x => x.Appearance.Shape = shape, SettingsSection.Appearance);
                break;
            case "notify":
                var parts = args.Split('|', 2);
                s.Bus.Publish(new CustomEvent(parts[0], parts.Length > 1 ? parts[1] : null));
                break;
            case "volume":
                if (double.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var level))
                    s.Bus.Publish(new VolumeEvent(level, false));
                break;
            case "capture":
                s.Settings.Update(x => x.Behavior.ExcludeFromScreenCapture = args.Trim() != "off", SettingsSection.Behavior);
                break;
            case "preview":
                if (Enum.TryParse<AnimationTheme>(args.Trim(), true, out var preview)) host.Notch.PreviewTheme(preview);
                break;
            case "settings":
                host.OpenSettings(string.IsNullOrWhiteSpace(args) ? null : args.Trim());
                break;
            case "perf":
                Nova.Core.Logging.Log.Info($"PERF state={sm.Snapshot.State} media={sm.Snapshot.HasMedia} timer={sm.Snapshot.HasTimer} frameClock={Notch.Rendering.FrameClock.IsRunning} subscribers={Notch.Rendering.FrameClock.SubscriberCount} playing={s.Media.Current?.IsPlaying} | {host.Notch.Diagnostics}");
                break;
            case "timer":
                if (double.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    s.Timer.Cancel();
                    s.Timer.SetDuration(TimeSpan.FromSeconds(seconds));
                    s.Timer.Label = "Debug timer";
                    s.Timer.Start();
                }
                break;
        }
    }
}
