using System.Diagnostics;

namespace Nova.Core.QuickApps;

public enum QuickAppKind
{
    /// <summary>A .exe file on disk.</summary>
    Executable,
    /// <summary>A .lnk / .url shortcut.</summary>
    Shortcut,
    /// <summary>An entry of the Windows "AppsFolder" (packaged/Store apps and Start menu apps), identified by its parsing name / AUMID.</summary>
    AppsFolder,
    /// <summary>A folder opened in File Explorer.</summary>
    Folder,
    /// <summary>A URI such as https:// or ms-settings:.</summary>
    Uri,
}

public sealed class QuickApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public QuickAppKind Kind { get; set; }
    public string Target { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? CustomIconPath { get; set; }
    public string? Hotkey { get; set; }

    public QuickApp Clone() => (QuickApp)MemberwiseClone();
}

public enum LaunchValidation { Ok, Missing, Invalid }

public interface IProcessStarter
{
    /// <summary>Starts a process. Returns false and an error message if the OS refused.</summary>
    bool TryStart(ProcessStartInfo info, out string? error);
}

public interface IFileSystemProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

public sealed class RealFileSystemProbe : IFileSystemProbe
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

public readonly record struct LaunchResult(bool Success, string? Error)
{
    public static LaunchResult Ok => new(true, null);
    public static LaunchResult Fail(string error) => new(false, error);
}

/// <summary>
/// Turns a <see cref="QuickApp"/> into a process launch. Pure logic around an injectable
/// <see cref="IProcessStarter"/> so it can be tested without starting real processes.
/// </summary>
public sealed class AppLauncher
{
    private readonly IProcessStarter _starter;
    private readonly IFileSystemProbe _fs;

    public AppLauncher(IProcessStarter starter, IFileSystemProbe? fs = null)
    {
        _starter = starter;
        _fs = fs ?? new RealFileSystemProbe();
    }

    public event Action<QuickApp>? Launched;

    public LaunchValidation Validate(QuickApp app)
    {
        if (string.IsNullOrWhiteSpace(app.Target)) return LaunchValidation.Invalid;
        var target = Environment.ExpandEnvironmentVariables(app.Target.Trim());
        switch (app.Kind)
        {
            case QuickAppKind.Executable:
            case QuickAppKind.Shortcut:
                if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return LaunchValidation.Invalid;
                // Bare file names (e.g. "notepad.exe") are resolved by the shell via PATH / App Paths.
                if (!Path.IsPathRooted(target)) return LaunchValidation.Ok;
                return _fs.FileExists(target) ? LaunchValidation.Ok : LaunchValidation.Missing;
            case QuickAppKind.Folder:
                return _fs.DirectoryExists(target) ? LaunchValidation.Ok : LaunchValidation.Missing;
            case QuickAppKind.AppsFolder:
                return target.Contains('\n') || target.Contains('"') ? LaunchValidation.Invalid : LaunchValidation.Ok;
            case QuickAppKind.Uri:
                return System.Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile
                    ? LaunchValidation.Ok : LaunchValidation.Invalid;
            default:
                return LaunchValidation.Invalid;
        }
    }

    public ProcessStartInfo BuildStartInfo(QuickApp app)
    {
        var target = Environment.ExpandEnvironmentVariables(app.Target.Trim());
        var info = new ProcessStartInfo { UseShellExecute = true };
        switch (app.Kind)
        {
            case QuickAppKind.AppsFolder:
                info.FileName = "explorer.exe";
                info.Arguments = "shell:AppsFolder\\" + target;
                break;
            case QuickAppKind.Folder:
                info.FileName = "explorer.exe";
                info.Arguments = "\"" + target + "\"";
                break;
            default:
                info.FileName = target;
                if (!string.IsNullOrWhiteSpace(app.Arguments)) info.Arguments = app.Arguments;
                break;
        }

        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
            info.WorkingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory);
        else if (app.Kind == QuickAppKind.Executable && Path.IsPathRooted(target))
            info.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
        return info;
    }

    public LaunchResult Launch(QuickApp app)
    {
        switch (Validate(app))
        {
            case LaunchValidation.Missing:
                return LaunchResult.Fail($"{app.Name} can't be found at its saved location.");
            case LaunchValidation.Invalid:
                return LaunchResult.Fail($"{app.Name} has an invalid launch target.");
        }

        if (!_starter.TryStart(BuildStartInfo(app), out var error))
            return LaunchResult.Fail(error ?? $"Windows refused to start {app.Name}.");

        Launched?.Invoke(app);
        return LaunchResult.Ok;
    }
}
