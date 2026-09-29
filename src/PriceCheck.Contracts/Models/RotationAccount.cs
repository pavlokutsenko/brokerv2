using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class RotationAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoginName { get; set; } = "";
    public string? LoginPasswordProtected { get; set; }
    [JsonIgnore]
    public string LoginPassword { get; set; } = "";
}
