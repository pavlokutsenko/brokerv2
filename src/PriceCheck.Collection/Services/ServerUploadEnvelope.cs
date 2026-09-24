using System.Text.Json;

namespace PriceCheck.Collector.Services;

public sealed class ServerUploadEnvelope
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Kind { get; set; }
    public required string Url { get; set; }
    public required JsonElement Body { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
