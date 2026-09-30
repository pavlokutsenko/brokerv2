using System.ComponentModel;
using System.Runtime.CompilerServices;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;

namespace PriceCheck.Launcher;

public sealed class LaunchRuntime : INotifyPropertyChanged
{
    public required CollectorProfile Profile { get; init; }
    private ClientSession? _session;
    private string _launchStatus = "Остановлено", _rotationStatus = "Ротация персонажей выключена";
    private bool _busy;
    private int _accountProcessCount;
    private ClientProtectionStatus _protection = ClientProtectionStatus.Pending;
    public ClientProtectionStatus Protection { get => _protection; set { _protection = value; Changed(); } }
    public ClientSession? Session { get => _session; set { _session = value; Changed(); Changed(nameof(ProcessLabel)); Changed(nameof(CanEditMarket)); } }
    public int AccountProcessCount { get=>_accountProcessCount; set { _accountProcessCount=value; Changed();Changed(nameof(ProcessLabel));Changed(nameof(CanEditMarket)); } }
    public bool CanEditMarket => AccountProcessCount==0 && !IsBusy && Session is null;
    public string ProcessLabel => AccountProcessCount>1 ? $"{AccountProcessCount} клиентов" :
        Session is { } session ? $"PID {session.ProcessId}" : AccountProcessCount==1 ? "1 клиент" : "Нет процесса";
    public string RoleLabel => "ЛАУНЧЕР";
    public string Status => LaunchStatus;
    public string LaunchStatus { get => _launchStatus; set { _launchStatus = value; Changed(); Changed(nameof(Status)); } }
    public string CharacterRotationStatus { get => _rotationStatus; set { _rotationStatus = value; Changed(); } }
    public bool IsBusy { get => _busy; set { _busy = value; Changed(); Changed(nameof(CanEditMarket)); } }
    public string? ClientFault { get; set; }
    public void RefreshProfile() => Changed(nameof(Profile));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
