using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

const string uuid = "01234567-89ab-cdef-0123-456789abcdef";
const string firstMonitor = @"ABC1234\5&11223344&0&UID9876";
var edid = new byte[256];
new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }.CopyTo(edid, 0);
edid[8] = 0x12; edid[9] = 0x34; edid[10] = 0x56; edid[11] = 0x78;
edid[12] = 1; edid[18] = 1; edid[19] = 4;
edid[57] = 0xFF; edid[59] = (byte)'O'; edid[60] = (byte)'L'; edid[61] = (byte)'D'; edid[62] = 10;
edid[75] = 0xFC; edid[77] = (byte)'N'; edid[78] = 10;
edid[126] = 1; edid[128] = 2; edid[130] = 4;
Checksum(edid, 0); Checksum(edid, 128);
Check(MonitorIdentityPreview.IsValid(edid), "fixture checksum");
var mapped = MonitorIdentityPreview.Map(uuid, firstMonitor, edid);
Check(MonitorIdentityPreview.IsValid(mapped), "mapped checksum");
Check(!mapped.AsSpan(12, 4).SequenceEqual(edid.AsSpan(12, 4)), "serial must change");
Check(mapped.AsSpan(128).SequenceEqual(edid.AsSpan(128)), "extension changed");
for (var i = 0; i < 256; i++)
    if (i is not (>= 12 and <= 15) && i is not (>= 59 and <= 71) && i != 127)
        Check(edid[i] == mapped[i], "timing/product/name byte changed: " + i);
Check(mapped.SequenceEqual(MonitorIdentityPreview.Map(uuid.ToUpperInvariant(), firstMonitor.ToLowerInvariant(), edid)), "case normalization");
Check(mapped.SequenceEqual(MonitorIdentityPreview.Map(uuid, firstMonitor, mapped)), "EDID mapping must be idempotent");
Check(!mapped.SequenceEqual(MonitorIdentityPreview.Map(uuid, firstMonitor + "2", edid)), "monitor instances share identity");
var malformed = (byte[])edid.Clone(); malformed[127]++;
Check(malformed.SequenceEqual(MonitorIdentityPreview.Map(uuid, firstMonitor, malformed)), "invalid checksum modified");
var truncated = edid[..128];
Check(!MonitorIdentityPreview.IsValid(truncated), "missing extension accepted");
Check(truncated.SequenceEqual(MonitorIdentityPreview.Map(uuid, firstMonitor, truncated)), "truncated EDID modified");
var mac = AdapterIdentityPreview.Map("02AABBCCDDEE", "{01234567-89ab-cdef-0123-456789abcdef}");
Check(mac == AdapterIdentityPreview.Map("02aabbccddee", "01234567-89AB-CDEF-0123-456789ABCDEF"), "MAC normalization");
Check((Convert.FromHexString(mac)[0] & 3) == 2, "MAC unicast/local bits");
Check(Enumerable.Range(0, 400).Select(i => AdapterIdentityPreview.Map("02AABBCCDDEE", "adapter-" + i)).Distinct().Count() == 400, "MAC mapping wraps after 256");
var template = new LaunchTemplate { Identity = ClientLaunchConfiguration.GenerateIdentity() };
var profileRow = new HardwareScanRow("Windows · HwProfileGuid 0042", "original", "RegQueryValueEx · hooked") { RegistryValueName = "HwProfileGuid" };
Check(IdentityPreviewService.WithTarget(profileRow, template, 0).TargetValue == template.Identity.HardwareProfileGuid, "non-0001 profile missing");
var secondCpu = new HardwareScanRow("WMI · Processor ID 2", "original", "IWbemClassObject::Get · hooked");
Check(IdentityPreviewService.WithTarget(secondCpu, template, 0).TargetValue == template.Identity.ProcessorId, "second processor missing");
if (args.Length > 0)
{
    foreach (var source in new[] { edid, malformed, truncated })
    {
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add(uuid); start.ArgumentList.Add(firstMonitor); start.ArgumentList.Add(Convert.ToHexString(source));
        using var native = Process.Start(start)!;
        var output = native.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        native.WaitForExit();
        Check(native.ExitCode == 0 && output[0] == Convert.ToHexString(MonitorIdentityPreview.Map(uuid, firstMonitor, source)), "native EDID differs from preview");
        Check(output[1] == Convert.ToHexString(MonitorIdentityPreview.Map(uuid, firstMonitor, source)), "kernel EDID differs from preview");
        Check(output[2] == mac, "native MAC differs from preview");
    }
    // Two processes use different template values through the same public APIs.
    foreach (var seed in new[] { uuid, Guid.NewGuid().ToString("D") })
    {
        var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add("--registry"); start.ArgumentList.Add(seed);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0 && output.Contains("PASS"), "registry API variants differ from template");
        foreach (var record in output.Split('\n').Where(line => line.StartsWith("WMI_MAC|")))
        {
            var fields = record.Trim().Split('|');
            var expected = AdapterIdentityPreview.Map("02AABBCCDDEE", fields[1]);
            Check(fields[2].Replace(":", "") == expected, "WMI adapter identity differs from network API");
        }
        foreach (var record in output.Split('\n').Where(line => line.StartsWith("WMI_MONITOR|")))
        {
            var fields = record.Trim().Split('|');
            var instance = fields[1].StartsWith(@"DISPLAY\", StringComparison.OrdinalIgnoreCase) ? fields[1][8..] : fields[1];
            instance = System.Text.RegularExpressions.Regex.Replace(instance, @"_\d+$", "");
            var expected = MonitorIdentityPreview.SerialText(seed, instance);
            expected = expected[..Math.Min(12, Math.Max(0, int.Parse(fields[2]) - 1))];
            Check(fields[3] == expected, "WMI monitor identity differs from EDID");
        }
    }
}
var scan = HardwareInventoryService.Scan();
Check(scan.Where(row => row.RegistryValueName == "EDID").All(row => row.SourceId.Length > 0 && row.Label.Contains(row.SourceId)), "monitor instance lost");
Check(scan.Where(row => row.Label.StartsWith("MAC · ")).All(row => row.SourceId.Length > 0), "adapter ID lost");
Check(scan.Any(row => row.SourceId == "Win32_ComputerSystemProduct.UUID"), "local WMI inventory failed");
Console.WriteLine($"HardwareIdentity.Smoke PASS: native parity, EDID preservation, MAC stability. Scan: {scan.Count} rows, " +
    $"{scan.Count(row => row.RegistryValueName == "EDID")} monitors, {scan.Count(row => row.Label.StartsWith("MAC · "))} adapters.");
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Checksum(byte[] data, int offset) { data[offset + 127] = unchecked((byte)-data.AsSpan(offset, 127).ToArray().Sum(value => (int)value)); }
