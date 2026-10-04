using Nova.App.Infrastructure;
using Nova.Core.Settings;
using Nova.Platform.Services;
using Velopack;

namespace Nova.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles installer/updater hooks (install, uninstall, update) and exits when needed.
        VelopackApp.Build()
            // Leave no trace on uninstall: remove the "start with Windows" entry.
            .OnBeforeUninstallFastCallback(_ => new StartupManager(new StartupRegistry(), Environment.ProcessPath ?? "NOVA.exe").SetEnabled(false))
            .Run();

        var options = LaunchOptions.Parse(args);
        using var instance = new SingleInstance("NOVA-7F3C2A");
        if (!instance.IsFirst)
        {
            instance.Send(options.ToPipeCommand());
            return 0;
        }
        if (options.Quit) return 0;

        var app = new App(options, instance);
        app.InitializeComponent();
        return app.Run();
    }
}
