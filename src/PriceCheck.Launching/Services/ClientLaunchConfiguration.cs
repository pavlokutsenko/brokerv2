using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public static class ClientLaunchConfiguration
{
    public static bool IsEnabled(LaunchTemplate? template) =>
        template is { HardwareEnabled: true } or { ProxyEnabled: true };

    public static string? Apply(ProcessStartInfo start, LaunchTemplate? template, Guid profileId)
    {
        if (!IsEnabled(template)) return null;
        if (template!.ProxyEnabled) ValidateProxy(template);
        if (!template.HardwareEnabled) return null;
        start.UseShellExecute = false;
        if (template.HardwareEnabled)
        {
            if (template.RotateEachLaunch) template.Identity = GenerateIdentity();
            AddIdentity(start, template.Identity);
            start.Environment["PRICECHECK_WORLD_IDENTITY"] =
                WorldIdentityConfiguration.Resolve(profileId, template.Identity.WorldIdentitySeed);
        }
        return ClientLaunchDeployment.PrepareAgentFor(start.FileName);
    }

    public static LaunchIdentity GenerateIdentity()
    {
        var identity = new LaunchIdentity
        {
            WorldIdentitySeed = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            SystemUuid = Guid.NewGuid().ToString("D"),
            MachineGuid = Guid.NewGuid().ToString("D"),
            BoardSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            VolumeSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            SystemSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
            ChassisSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
            ProcessorId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            ProcessorSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
            ProcessorModel = ReadProcessorModel() + " " + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            ProcessorRevision = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            MemorySerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            DiskSerial = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            HardwareProfileGuid = Guid.NewGuid().ToString("B"),
            WindowsProductId = $"00330-{RandomNumberGenerator.GetInt32(100000):D5}-{RandomNumberGenerator.GetInt32(100000):D5}-AAOEM",
            DiskGuid = Guid.NewGuid().ToString("D"),
            DiskSignature = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            SusClientId = Guid.NewGuid().ToString("D"),
            SqmMachineId = Guid.NewGuid().ToString("B"),
            VideoIdentifier = Guid.NewGuid().ToString("B"),
            RegistryComputerName = "PC-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            InstallDate = (uint)RandomNumberGenerator.GetInt32(1_500_000_000, 1_750_000_000),
            RouterMac = NewMacAddress()
        };
        identity.MacAddress = NewMacAddress();
        return identity;
    }

    private static string NewMacAddress()
    {
        var mac = RandomNumberGenerator.GetBytes(6);
        mac[0] = (byte)((mac[0] | 0x02) & 0xFE);
        return Convert.ToHexString(mac);
    }

    private static string ReadProcessorModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var model = new string((key?.GetValue("ProcessorNameString") as string ?? "")
                .Where(ch => ch is >= ' ' and <= '~').ToArray()).Trim();
            return string.IsNullOrWhiteSpace(model) ? "Processor" : model[..Math.Min(model.Length, 110)];
        }
        catch { return "Processor"; }
    }

    private static byte[] StableBytes(string seed, string field) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + field));

    public static void FillMissingIdentity(LaunchIdentity identity)
    {
        var generated = GenerateIdentity();
        var stableSeed = string.IsNullOrEmpty(identity.WorldIdentitySeed) ? identity.SystemUuid : identity.WorldIdentitySeed;
        if (string.IsNullOrEmpty(stableSeed)) stableSeed = generated.WorldIdentitySeed;
        if (string.IsNullOrEmpty(identity.SystemUuid)) identity.SystemUuid = generated.SystemUuid;
        if (string.IsNullOrEmpty(identity.MachineGuid)) identity.MachineGuid = generated.MachineGuid;
        if (string.IsNullOrEmpty(identity.BoardSerial)) identity.BoardSerial = generated.BoardSerial;
        if (string.IsNullOrEmpty(identity.VolumeSerial)) identity.VolumeSerial = generated.VolumeSerial;
        if (string.IsNullOrEmpty(identity.MacAddress)) identity.MacAddress = generated.MacAddress;
        if (string.IsNullOrEmpty(identity.SystemSerial)) identity.SystemSerial = generated.SystemSerial;
        if (string.IsNullOrEmpty(identity.ChassisSerial)) identity.ChassisSerial = generated.ChassisSerial;
        if (string.IsNullOrEmpty(identity.ProcessorId)) identity.ProcessorId = generated.ProcessorId;
        if (string.IsNullOrEmpty(identity.ProcessorSerial)) identity.ProcessorSerial = generated.ProcessorSerial;
        if (string.IsNullOrEmpty(identity.ProcessorModel))
            identity.ProcessorModel = "Processor " + Convert.ToHexString(StableBytes(stableSeed, "model").AsSpan(0, 4));
        if (string.IsNullOrEmpty(identity.ProcessorRevision))
            identity.ProcessorRevision = Convert.ToHexString(StableBytes(stableSeed, "revision").AsSpan(0, 8));
        if (string.IsNullOrEmpty(identity.MemorySerial)) identity.MemorySerial = generated.MemorySerial;
        if (string.IsNullOrEmpty(identity.DiskSerial)) identity.DiskSerial = generated.DiskSerial;
        if (string.IsNullOrEmpty(identity.HardwareProfileGuid)) identity.HardwareProfileGuid = generated.HardwareProfileGuid;
        if (string.IsNullOrEmpty(identity.WindowsProductId)) identity.WindowsProductId = generated.WindowsProductId;
        if (string.IsNullOrEmpty(identity.DiskGuid)) identity.DiskGuid = generated.DiskGuid;
        if (string.IsNullOrEmpty(identity.DiskSignature)) identity.DiskSignature = generated.DiskSignature;
        if (string.IsNullOrEmpty(identity.SusClientId)) identity.SusClientId = generated.SusClientId;
        if (string.IsNullOrEmpty(identity.SqmMachineId))
            identity.SqmMachineId = new Guid(StableBytes(stableSeed, "sqm").AsSpan(0, 16)).ToString("B");
        if (string.IsNullOrEmpty(identity.VideoIdentifier)) identity.VideoIdentifier = generated.VideoIdentifier;
        if (string.IsNullOrEmpty(identity.RegistryComputerName)) identity.RegistryComputerName = generated.RegistryComputerName;
        if (identity.InstallDate == 0) identity.InstallDate = generated.InstallDate;
        if (string.IsNullOrEmpty(identity.RouterMac)) identity.RouterMac = generated.RouterMac;
    }

    private static void AddIdentity(ProcessStartInfo start, LaunchIdentity identity)
    {
        ValidateIdentity(identity);
        start.Environment["PRICECHECK_HW_UUID"] = identity.SystemUuid;
        start.Environment["PRICECHECK_HW_MACHINE_GUID"] = identity.MachineGuid;
        start.Environment["PRICECHECK_HW_BOARD_SERIAL"] = identity.BoardSerial;
        start.Environment["PRICECHECK_HW_VOLUME_SERIAL"] = identity.VolumeSerial;
        start.Environment["PRICECHECK_HW_MAC"] = identity.MacAddress;
        start.Environment["PRICECHECK_HW_SYSTEM_SERIAL"] = identity.SystemSerial;
        start.Environment["PRICECHECK_HW_CHASSIS_SERIAL"] = identity.ChassisSerial;
        start.Environment["PRICECHECK_HW_PROCESSOR_ID"] = identity.ProcessorId;
        start.Environment["PRICECHECK_HW_PROCESSOR_SERIAL"] = identity.ProcessorSerial;
        start.Environment["PRICECHECK_HW_PROCESSOR_MODEL"] = identity.ProcessorModel;
        start.Environment["PRICECHECK_HW_PROCESSOR_REVISION"] = identity.ProcessorRevision;
        start.Environment["PRICECHECK_HW_MEMORY_SERIAL"] = identity.MemorySerial;
        start.Environment["PRICECHECK_HW_DISK_SERIAL"] = identity.DiskSerial;
        start.Environment["PRICECHECK_HW_PROFILE_GUID"] = identity.HardwareProfileGuid;
        start.Environment["PRICECHECK_HW_PRODUCT_ID"] = identity.WindowsProductId;
        start.Environment["PRICECHECK_HW_DISK_GUID"] = identity.DiskGuid;
        start.Environment["PRICECHECK_HW_DISK_SIGNATURE"] = identity.DiskSignature;
        start.Environment["PRICECHECK_HW_SUS_CLIENT_ID"] = identity.SusClientId;
        start.Environment["PRICECHECK_HW_SQM_MACHINE_ID"] = identity.SqmMachineId;
        start.Environment["PRICECHECK_HW_VIDEO_ID"] = identity.VideoIdentifier;
        start.Environment["PRICECHECK_HW_COMPUTER_NAME"] = identity.RegistryComputerName;
        start.Environment["PRICECHECK_HW_INSTALL_DATE"] = identity.InstallDate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PRICECHECK_HW_ROUTER_MAC"] = identity.RouterMac;
        var gateway = GatewayIdentityService.FindIpv4Gateway();
        if (gateway is not null)
            start.Environment["PRICECHECK_HW_ROUTER_IP"] = BitConverter.ToUInt32(gateway.GetAddressBytes()).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static void ValidateIdentity(LaunchIdentity identity)
    {
        if (!Guid.TryParse(identity.SystemUuid, out _) || !Guid.TryParse(identity.MachineGuid, out _) ||
            identity.BoardSerial.Length == 0 || !uint.TryParse(identity.VolumeSerial,
                System.Globalization.NumberStyles.HexNumber, null, out _) ||
            identity.MacAddress.Length != 12 || !identity.MacAddress.All(Uri.IsHexDigit))
            throw new ArgumentException("The template contains invalid HWID values. Click Regenerate.");
        if (!Guid.TryParse(identity.HardwareProfileGuid, out _) || !Guid.TryParse(identity.DiskGuid, out _) ||
            !Guid.TryParse(identity.SusClientId, out _) || !Guid.TryParse(identity.SqmMachineId, out _) ||
            !Guid.TryParse(identity.VideoIdentifier, out _) ||
            identity.ProcessorId.Length != 16 || !identity.ProcessorId.All(Uri.IsHexDigit) ||
            identity.ProcessorRevision.Length != 16 || !identity.ProcessorRevision.All(Uri.IsHexDigit) ||
            identity.DiskSignature.Length != 8 || !identity.DiskSignature.All(Uri.IsHexDigit) ||
            identity.RouterMac.Length != 12 || !identity.RouterMac.All(Uri.IsHexDigit) || identity.InstallDate == 0 ||
            new[] { identity.SystemSerial, identity.ChassisSerial, identity.ProcessorSerial,
                identity.MemorySerial, identity.DiskSerial, identity.WindowsProductId,
                identity.RegistryComputerName, identity.ProcessorModel }
                .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(ch => ch < 32 || ch > 126)))
            throw new ArgumentException("The template contains invalid additional identifiers. Click Regenerate.");
    }

    public static void ValidateProxy(LaunchTemplate template)
    {
        var host = template.ProxyHost.Trim();
        if (string.IsNullOrEmpty(host) || host.Any(char.IsWhiteSpace) || host.Contains(':'))
            throw new ArgumentException("Enter an HTTP proxy hostname or IPv4 address without a scheme or port.");
        if (template.ProxyPort is < 1 or > 65535)
            throw new ArgumentException("HTTP proxy port must be between 1 and 65535.");
        if (string.IsNullOrEmpty(template.ProxyUser) || string.IsNullOrEmpty(template.ProxyPassword) ||
            template.ProxyUser.Contains(':') || HasLineBreak(template.ProxyUser) || HasLineBreak(template.ProxyPassword))
            throw new ArgumentException("Enter the HTTP proxy username and password. The username cannot contain a colon, and neither field can contain a line break.");
    }

    private static bool HasLineBreak(string text) => text.Contains('\r') || text.Contains('\n');
}
