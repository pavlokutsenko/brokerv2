using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

namespace PriceCheck.Collector.Services;

internal static class ClientLaunchDeployment
{
    private static readonly object Gate = new();
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PriceCheckCollector", "client-launch-deployments.json");

    public static string PrepareAgentFor(string executable)
    {
        lock (Gate) return PrepareCore(executable);
    }

    private static string PrepareCore(string executable)
    {
        var runtime = Path.Combine(AppContext.BaseDirectory, "ClientLaunchRuntime");
        var agent = Path.Combine(runtime, "PriceCheck.ClientAgent.dll");
        if (!File.Exists(agent)) throw new FileNotFoundException("Launch agent is missing from the package.", agent);
        var directory = Path.GetDirectoryName(executable) ?? throw new ArgumentException("Invalid client path.");
        var target = Path.GetFullPath(Path.Combine(directory, "version.dll"));
        var records = LoadRecords();
        if (records.TryGetValue(target, out var recordedHash))
        {
            if (File.Exists(target) && Hash(target) == recordedHash)
            {
                if (Process.GetProcessesByName("lu4.bin").Length > 0 ||
                    Process.GetProcessesByName("lu4-win64-shipping").Length > 0)
                    throw new IOException("Stop all LU4 clients before removing the previously installed version.dll.");
                File.Delete(target);
            }
            records.Remove(target);
            SaveRecords(records);
        }
        return agent;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Dictionary<string, string> LoadRecords()
    {
        var saved = File.Exists(StatePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StatePath))
            : null;
        return saved is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveRecords(Dictionary<string, string> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records));
        File.Move(temporary, StatePath, true);
    }
}
