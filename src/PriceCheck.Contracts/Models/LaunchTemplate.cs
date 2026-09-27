using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PriceCheck.Collector.Models;

public sealed class LaunchIdentity
{
    public string WorldIdentitySeed { get; set; } = "";
    public string SystemUuid { get; set; } = "";
    public string MachineGuid { get; set; } = "";
    public string BoardSerial { get; set; } = "";
    public string VolumeSerial { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string SystemSerial { get; set; } = "";
    public string ChassisSerial { get; set; } = "";
    public string ProcessorId { get; set; } = "";
    public string ProcessorSerial { get; set; } = "";
    public string MemorySerial { get; set; } = "";
    public string DiskSerial { get; set; } = "";
    public string HardwareProfileGuid { get; set; } = "";
    public string WindowsProductId { get; set; } = "";
    public string DiskGuid { get; set; } = "";
    public string DiskSignature { get; set; } = "";
    public string SusClientId { get; set; } = "";
    public string VideoIdentifier { get; set; } = "";
    public string RegistryComputerName { get; set; } = "";
    public uint InstallDate { get; set; }
    public string RouterMac { get; set; } = "";
}

public sealed class LaunchTemplate : INotifyPropertyChanged
{
    private string _name = "New template";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); }
    }
    public string Description { get; set; } = "";
    public bool HardwareEnabled { get; set; } = true;
    public bool RotateEachLaunch { get; set; }
    public LaunchIdentity Identity { get; set; } = new();
    public bool ProxyEnabled { get; set; }
    public string ProxyHost { get; set; } = "";
    public int ProxyPort { get; set; }
    public string ProxyUser { get; set; } = "";
    public string? ProxyPasswordProtected { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string ProxyPassword { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => Id == Guid.Empty ? "Запуск запрещён: выберите шаблон HWID" :
        (!HardwareEnabled ? "Запуск запрещён: включите HWID · " : "") +
        $"HWID: {(HardwareEnabled ? RotateEachLaunch ? "new on each launch" : "fixed" : "off")}  ·  " +
        $"HTTP proxy: {(ProxyEnabled ? $"{ProxyHost}:{ProxyPort}" : "off")}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
