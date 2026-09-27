using System.ComponentModel;
using System.Runtime.CompilerServices;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;

namespace PriceCheck.Launcher;

public sealed class LaunchRuntime : INotifyPropertyChanged
{
    public required CollectorProfile Profile { get; init; }
    private ClientSession? _session;
    private string _launchStatus = "Stopped", _rotationStatus = "Character rotation is off";
    private bool _busy;
    private ClientProtectionStatus _protection = ClientProtectionStatus.Pending;
    public ClientProtectionStatus Protection { get => _protection; set { _protection = value; Changed(); } }
    public ClientSession? Session { get => _session; set { _session = value; Changed(); Changed(nameof(ProcessLabel)); } }
    public string ProcessLabel => Session is { } session ? $"PID {session.ProcessId}" : "No process";
    public string LaunchStatus { get => _launchStatus; set { _launchStatus = value; Changed(); } }
    public string CharacterRotationStatus { get => _rotationStatus; set { _rotationStatus = value; Changed(); } }
    public bool IsBusy { get => _busy; set { _busy = value; Changed(); } }
    public string? ClientFault { get; set; }
    public void RefreshProfile() => Changed(nameof(Profile));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
