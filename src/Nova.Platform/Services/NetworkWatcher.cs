using Nova.Core.Logging;
using Windows.Networking.Connectivity;

namespace Nova.Platform.Services;

/// <summary>Internet connectivity changes via WinRT NetworkInformation (event driven, debounced).</summary>
public sealed class NetworkWatcher : IDisposable
{
    private Timer? _debounce;
    private bool _started;

    public event Action<bool, string?, bool>? ConnectionChanged;
    public event Action? StatusChanged;

    public bool Connected { get; private set; }
    public string? NetworkName { get; private set; }
    public bool IsWireless { get; private set; }
    public byte SignalBars { get; private set; }

    public void Start()
    {
        try
        {
            Read();
            _debounce = new Timer(_ => Evaluate(), null, Timeout.Infinite, Timeout.Infinite);
            NetworkInformation.NetworkStatusChanged += OnStatusChanged;
            _started = true;
        }
        catch (Exception ex) { Log.Warn("NetworkInformation unavailable", ex); }
    }

    private void OnStatusChanged(object sender) => _debounce?.Change(1500, Timeout.Infinite);

    private void Read()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        var level = profile?.GetNetworkConnectivityLevel() ?? NetworkConnectivityLevel.None;
        Connected = level is NetworkConnectivityLevel.InternetAccess or NetworkConnectivityLevel.ConstrainedInternetAccess;
        IsWireless = profile?.IsWlanConnectionProfile ?? false;
        NetworkName = profile?.ProfileName;
        SignalBars = profile?.GetSignalBars() ?? 0;
    }

    private void Evaluate()
    {
        try
        {
            var (wasConnected, wasName) = (Connected, NetworkName);
            Read();
            StatusChanged?.Invoke();
            if (wasConnected != Connected || (Connected && wasName != NetworkName))
                ConnectionChanged?.Invoke(Connected, NetworkName, IsWireless);
        }
        catch (Exception ex) { Log.Warn("Network status read failed", ex); }
    }

    public void Dispose()
    {
        if (_started) NetworkInformation.NetworkStatusChanged -= OnStatusChanged;
        _debounce?.Dispose();
        _started = false;
    }
}
