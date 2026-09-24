using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public static class IdentityPreviewService
{
    public static HardwareScanRow WithTarget(HardwareScanRow row, LaunchTemplate? template, int macIndex)
    {
        if (template?.HardwareEnabled != true) return row with { TargetValue = "No override" };
        var identity = template.Identity;
        string? target = row.Label switch
        {
            "System · UUID" => identity.SystemUuid,
            "System · serial" => Adapt(identity.SystemSerial, row.CurrentValue.Length),
            "Baseboard · serial" => Adapt(identity.BoardSerial, row.CurrentValue.Length),
            "Chassis · serial" => Adapt(identity.ChassisSerial, row.CurrentValue.Length),
            "Processor · ID" => identity.ProcessorId,
            "Processor · serial" => Adapt(identity.ProcessorSerial, row.CurrentValue.Length),
            "Windows · MachineGuid" => identity.MachineGuid,
            "Windows · HwProfileGuid" => identity.HardwareProfileGuid,
            "Windows · ProductId" => identity.WindowsProductId,
            "Windows · SusClientId" => identity.SusClientId,
            "Windows · VideoIdentifier" => identity.VideoIdentifier,
            "Windows · PC name (registry)" => identity.RegistryComputerName,
            "Windows · InstallDate" => identity.InstallDate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ when row.Label.StartsWith("Memory ", StringComparison.Ordinal) => Adapt(identity.MemorySerial, row.CurrentValue.Length),
            _ when row.Label.StartsWith("Disk ", StringComparison.Ordinal) && row.Label.Contains("serial") =>
                Adapt(identity.DiskSerial, row.CurrentValue.Length),
            _ when row.Label.StartsWith("Disk ", StringComparison.Ordinal) && row.Label.EndsWith("GPT GUID") => identity.DiskGuid,
            _ when row.Label.StartsWith("Disk ", StringComparison.Ordinal) && row.Label.EndsWith("MBR ID") => identity.DiskSignature,
            _ when row.Label.StartsWith("Volume ", StringComparison.Ordinal) => identity.VolumeSerial,
            _ when row.Label.StartsWith("MAC · ", StringComparison.Ordinal) => MacForAdapter(identity.MacAddress, macIndex),
            _ when row.Label.StartsWith("Gateway ", StringComparison.Ordinal) && row.Coverage == "SendARP · gateway hook" => identity.RouterMac,
            _ => null
        };
        return row with { TargetValue = target ?? "Not overridden" };
    }

    private static string Adapt(string seed, int length)
    {
        if (seed.Length == 0 || length <= 0) return "—";
        return new string(Enumerable.Range(0, length).Select(index => seed[index % seed.Length]).ToArray());
    }

    private static string MacForAdapter(string value, int index)
    {
        if (value.Length != 12 || !byte.TryParse(value[10..], System.Globalization.NumberStyles.HexNumber,
                null, out var last)) return value;
        return value[..10] + unchecked((byte)(last + index)).ToString("X2");
    }
}
