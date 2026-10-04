using System.Windows;
using System.Windows.Threading;
using Nova.App.Infrastructure;
using Nova.Core.Logging;

namespace Nova.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstance _instance;
    private AppHost? _host;

    public App(LaunchOptions options, SingleInstance instance)
    {
        _options = options;
        _instance = instance;
    }

    public static AppHost Host => ((App)Current)._host ?? throw new InvalidOperationException("NOVA is not started");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception (fatal)", args.ExceptionObject as Exception);
            Log.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            _host = new AppHost(_options, _instance);
            _host.Start();
        }
        catch (Exception ex)
        {
            Log.Error("NOVA failed to start", ex);
            Log.Flush();
            MessageBox.Show($"NOVA couldn't start:\n\n{ex.Message}\n\nDetails were written to the log folder:\n{Log.Directory}",
                "NOVA", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background utility must not disappear because one feature failed.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _host?.Dispose(); }
        catch (Exception ex) { Log.Error("Error during shutdown", ex); }
        Log.Info("NOVA exited");
        Log.Shutdown();
        base.OnExit(e);
    }
}
