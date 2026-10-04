namespace Nova.App.Infrastructure;

/// <summary>Command line switches.</summary>
public sealed record LaunchOptions
{
    /// <summary>Started by Windows at sign-in: no windows, no greeting.</summary>
    public bool Background { get; init; }
    public bool OpenSettings { get; init; }
    public bool ForceOnboarding { get; init; }
    public bool ResetSettings { get; init; }
    public bool Quit { get; init; }
    /// <summary>Developer commands forwarded over the single-instance pipe (e.g. "state Media").</summary>
    public string? DebugCommand { get; init; }

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var o = new LaunchOptions();
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--background": o = o with { Background = true }; break;
                case "--settings": o = o with { OpenSettings = true }; break;
                case "--onboarding": o = o with { ForceOnboarding = true }; break;
                case "--reset-settings": o = o with { ResetSettings = true }; break;
                case "--quit": o = o with { Quit = true }; break;
                case "--debug":
                    if (i + 1 < args.Count) o = o with { DebugCommand = string.Join(' ', args.Skip(i + 1)) };
                    i = args.Count;
                    break;
            }
        }
        return o;
    }

    /// <summary>The message a second instance sends to the running one.</summary>
    public string ToPipeCommand()
    {
        if (Quit) return "quit";
        if (DebugCommand != null) return "debug " + DebugCommand;
        if (ForceOnboarding) return "onboarding";
        if (Background) return "noop";
        return "settings";
    }
}
