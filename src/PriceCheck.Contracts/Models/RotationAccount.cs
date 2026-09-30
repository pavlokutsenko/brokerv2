using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class RotationAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoginName { get; set; } = "";
    public string? LoginPasswordProtected { get; set; }
    [JsonIgnore]
    public string LoginPassword { get; set; } = "";
    public Guid LaunchTemplateId { get; set; }
    public bool? AutoLoginEnabled { get; set; }
    public int CharacterSlot { get; set; }
    public bool CharacterRotationEnabled { get; set; }
    public int RotationIntervalMinutes { get; set; } = 60;
    public int RotationJitterMinutes { get; set; } = 15;
    public bool AutoRestartEnabled { get; set; } = true;
}
