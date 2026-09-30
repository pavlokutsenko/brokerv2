using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Services;

public static class DriverIdentityService
{
    public static string Status { get; private set; } = "Драйвер HWID · ожидание первого клиента";

    public static void Bind(Process process, Guid profileId, LaunchIdentity identity)
    {
        using var device = new Lu4Device();
        var enabled = device.SetProcessIdentity(process.Id, process.StartTime.ToUniversalTime().ToFileTimeUtc(),
            profileId, identity.MachineGuid, identity.HardwareProfileGuid, identity.SystemUuid,
            new(identity.WindowsProductId, identity.SusClientId, identity.SqmMachineId, identity.VideoIdentifier,
                identity.RegistryComputerName, identity.ProcessorModel, identity.ProcessorRevision, identity.InstallDate));
        Status = enabled ? "Драйвер HWID · PID/шаблон + общий набор первого клиента" :
            "Драйвер HWID · старая сборка, доступна только подмена агентом";
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, "identity-driver.log"),
            $"{DateTimeOffset.UtcNow:O} pid={process.Id} profile={profileId} kernel={enabled}\n");
    }
}
