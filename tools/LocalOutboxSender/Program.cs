using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

// Operator-only durable outbox drain. No CollectionModule, reader, game process
// attachment, route creation, or synthetic successful reading is involved.
if (args.Length != 2 || args[0] != "--server-url")
    throw new ArgumentException("Usage: --server-url https://host/api");
var destination = args[1].TrimEnd('/');
if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
    uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
    throw new ArgumentException("An HTTPS destination without credentials is required.");
if (Process.GetProcessesByName("PriceCheck.Collector").Length > 0 ||
    Process.GetProcessesByName("BrokerWorker").Length > 0)
    throw new InvalidOperationException("Collector and its upload worker must be stopped.");
var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PriceCheckCollector", "profiles.json");
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
options.Converters.Add(new JsonStringEnumConverter());
var profiles = JsonSerializer.Deserialize<List<CollectorProfile>>(File.ReadAllText(path), options)
    ?? throw new InvalidDataException("Missing profile configuration");
if (profiles.Any(p => p.CollectionEnabled || p.ServerUrl.TrimEnd('/') != destination))
    throw new InvalidOperationException("Stop collection and complete profile destination migration first.");
foreach (var profile in profiles.DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
{
    using var store = new LocalCycleStore(profile);
    var initial = store.PendingUploads;
    if (initial == 0) { Console.WriteLine($"{profile.Name}: outbox empty"); continue; }
    Console.WriteLine($"{profile.Name}: sending {initial} durable individual operations");
    store.Message += Console.WriteLine;
    store.WakeSender();
    var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
    while (store.PendingUploads > 0 && DateTimeOffset.UtcNow < deadline)
        await Task.Delay(1000);
    var remaining = store.PendingUploads;
    Console.WriteLine($"{profile.Name}: remaining={remaining}");
    if (remaining != 0)
    {
        Environment.ExitCode = 2;
        return; // Dispose cancels/joins the sender, retaining every unacknowledged row.
    }
}
