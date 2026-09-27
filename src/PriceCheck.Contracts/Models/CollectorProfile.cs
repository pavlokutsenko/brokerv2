namespace PriceCheck.Collector.Models;

public sealed class CenterZoneSettings
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class CollectorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Gamma";
    public string City { get; set; } = "Giran";
    public CollectorRole Role { get; set; } = CollectorRole.BrokerRadar;
    public string ClientFolder { get; set; } = "";
    public string? LaunchFile { get; set; }
    public Guid LaunchTemplateId { get; set; }
    public bool AutoLoginEnabled { get; set; }
    public string LoginServerName { get; set; } = "Gamma";
    public int CharacterSlot { get; set; }
    public bool CharacterRotationEnabled { get; set; }
    public bool AutoRestartEnabled { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public int RotationCharacterCount { get; set; }
    // Selectable indices in the occupied Characters array returned by this
    // login. These are runtime observations, never account slot capacity.
    [System.Text.Json.Serialization.JsonIgnore]
    public int[] RotationCharacterSlots { get; set; } = [];
    public int RotationIntervalMinutes { get; set; } = 60;
    public int RotationJitterMinutes { get; set; } = 15;
    public string LoginName { get; set; } = "";
    public string? LoginPasswordProtected { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string LoginPassword { get; set; } = "";
    public bool GenerateHardwareIdentity { get; set; }
    public bool ProxyEnabled { get; set; }
    public string ProxyHost { get; set; } = "";
    public int ProxyPort { get; set; }
    public string ProxyUser { get; set; } = "";
    public string? ProxyPasswordProtected { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string ProxyPassword { get; set; } = "";
    public int BrokerIntervalMinutes { get; set; } = 5;
    public string ServerUrl { get; set; } = "https://pog-sandbox.com/api";
    private double _recheckHours = 24;
    public double RecheckHours
    {
        get => _recheckHours;
        set
        {
            if (!double.IsFinite(value) || value is < 0.1 or > 8760)
                throw new ArgumentOutOfRangeException(nameof(value), "Интервал должен быть от 0,1 до 8760 часов.");
            _recheckHours = value;
        }
    }
    public Dictionary<string, CenterZoneSettings> CenterZonesByCity { get; set; } = [];
    // Read-only runtime reuse of the single approved saved Giran point. This
    // must never create or overwrite a profile's persisted city settings.
    [System.Text.Json.Serialization.JsonIgnore]
    public CenterZoneSettings? SharedGiranCenter { get; internal set; }
    // Read only during one-time migration from CollectorNext profiles.
    public double? CenterZoneX { get; set; }
    public double? CenterZoneY { get; set; }
    public int? LastProcessId { get; set; }
    public DateTimeOffset? LastProcessStartUtc { get; set; }
    public bool CollectionEnabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
