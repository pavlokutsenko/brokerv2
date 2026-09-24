using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Net.NetworkInformation;

if (args is ["--world-identity-checks"]) { WorldIdentityChecks.Run(); return; }

if (args.Length >= 3 && args[0] == "--proxy-server")
{
    await ProxyServer.RunAsync(int.Parse(args[1]), args[2]);
    return;
}

if (args.Contains("--child"))
{
    var proxyDll = Path.Combine(AppContext.BaseDirectory, "version.dll");
    if (Native.LoadLibraryW(proxyDll) == IntPtr.Zero) throw new InvalidOperationException("version.dll did not load");
    var eventName = $@"Local\PriceCheckAgentReady_{Environment.ProcessId}";
    EventWaitHandle? ready = null;
    for (var i = 0; i < 100 && ready is null; i++)
    {
        try { ready = EventWaitHandle.OpenExisting(eventName); }
        catch (WaitHandleCannotBeOpenedException) { await Task.Delay(100); }
    }
    if (ready is null || !ready.WaitOne(5000)) throw new InvalidOperationException("agent did not signal ready");
    ready.Dispose();
    var machineGuid = Native.ReadMachineGuid();
    if (machineGuid != Environment.GetEnvironmentVariable("PRICECHECK_HW_MACHINE_GUID"))
        throw new InvalidOperationException($"MachineGuid mismatch: {machineGuid}");
    var hardwareProfile = Native.ReadRegistryValue(@"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles\0001", "HwProfileGuid");
    if (hardwareProfile != Environment.GetEnvironmentVariable("PRICECHECK_HW_PROFILE_GUID"))
        throw new InvalidOperationException($"HwProfileGuid mismatch: {hardwareProfile}");
    var productId = Native.ReadRegistryValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId");
    if (productId != Environment.GetEnvironmentVariable("PRICECHECK_HW_PRODUCT_ID"))
        throw new InvalidOperationException($"ProductId mismatch: {productId}");
    var computerName = Native.ReadRegistryValue(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName");
    if (computerName != Environment.GetEnvironmentVariable("PRICECHECK_HW_COMPUTER_NAME"))
        throw new InvalidOperationException($"ComputerName registry mismatch: {computerName}");
    var installDate = Native.ReadRegistryDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate");
    if (installDate.ToString() != Environment.GetEnvironmentVariable("PRICECHECK_HW_INSTALL_DATE"))
        throw new InvalidOperationException($"InstallDate registry mismatch: {installDate}");
    var gateway = NetworkInterface.GetAllNetworkInterfaces()
        .SelectMany(adapter => adapter.GetIPProperties().GatewayAddresses)
        .Select(value => value.Address)
        .FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetwork);
    if (gateway is not null && Native.ReadRouterMac(gateway) is { } routerMac &&
        routerMac != Environment.GetEnvironmentVariable("PRICECHECK_HW_ROUTER_MAC"))
        throw new InvalidOperationException($"SendARP MAC mismatch: {routerMac}");
    if (!Native.GetVolumeInformationW(@"C:\", null, 0, out var volume, out _, out _, null, 0))
        throw new InvalidOperationException("GetVolumeInformationW failed");
    var expectedVolume = Convert.ToUInt32(Environment.GetEnvironmentVariable("PRICECHECK_HW_VOLUME_SERIAL"), 16);
    if (volume != expectedVolume) throw new InvalidOperationException($"volume serial mismatch: {volume:X8}");
    var firmwareSize = Native.GetSystemFirmwareTable(0x52534D42, 0, null, 0);
    if (firmwareSize is 0 or > 1_000_000) throw new InvalidOperationException("SMBIOS table unavailable");
    var firmware = new byte[firmwareSize];
    if (Native.GetSystemFirmwareTable(0x52534D42, 0, firmware, firmwareSize) != firmwareSize)
        throw new InvalidOperationException("SMBIOS table read failed");
    var foundUuid = false;
    var foundProcessorId = false;
    var verifiedSerials = 0;
    var offset = 8;
    while (offset + 24 < firmware.Length)
    {
        var type = firmware[offset];
        var length = firmware[offset + 1];
        if (length < 4 || offset + length >= firmware.Length) break;
        if (type == 1 && length >= 24)
        {
            var uuid = new Guid(firmware.AsSpan(offset + 8, 16));
            foundUuid = uuid == Guid.Parse(Environment.GetEnvironmentVariable("PRICECHECK_HW_UUID")!);
        }
        if (type == 4 && length >= 16)
            foundProcessorId = Convert.ToHexString(firmware, offset + 8, 8) ==
                Environment.GetEnvironmentVariable("PRICECHECK_HW_PROCESSOR_ID");
        string? seed = type switch
        {
            1 when length > 7 => Environment.GetEnvironmentVariable("PRICECHECK_HW_SYSTEM_SERIAL"),
            2 when length > 7 => Environment.GetEnvironmentVariable("PRICECHECK_HW_BOARD_SERIAL"),
            3 when length > 7 => Environment.GetEnvironmentVariable("PRICECHECK_HW_CHASSIS_SERIAL"),
            4 when length > 0x20 => Environment.GetEnvironmentVariable("PRICECHECK_HW_PROCESSOR_SERIAL"),
            17 when length > 0x18 => Environment.GetEnvironmentVariable("PRICECHECK_HW_MEMORY_SERIAL"),
            _ => null
        };
        if (seed is not null)
        {
            var index = firmware[offset + (type == 4 ? 0x20 : type == 17 ? 0x18 : 7)];
            var value = ReadSmbiosString(firmware, offset, length, index);
            if (value.Length > 0)
            {
                var expected = new string(Enumerable.Range(0, value.Length)
                    .Select(i => seed[i % seed.Length]).ToArray());
                if (value != expected) throw new InvalidOperationException($"SMBIOS type {type} serial mismatch: {value}");
                verifiedSerials++;
            }
        }
        offset += length;
        while (offset + 1 < firmware.Length && (firmware[offset] != 0 || firmware[offset + 1] != 0)) offset++;
        offset += 2;
    }
    if (!foundUuid) throw new InvalidOperationException("SMBIOS UUID mismatch");
    if (!foundProcessorId) throw new InvalidOperationException("SMBIOS processor ID mismatch");
    if (verifiedSerials == 0) throw new InvalidOperationException("no SMBIOS serial field verified");
    if (Native.ReadDiskIdentity(0) is { } disk)
    {
        var seed = Environment.GetEnvironmentVariable("PRICECHECK_HW_DISK_SERIAL")!;
        var expectedSerial = new string(Enumerable.Range(0, disk.Serial.Length)
            .Select(index => seed[index % seed.Length]).ToArray());
        if (disk.Serial != expectedSerial) throw new InvalidOperationException($"disk serial mismatch: {disk.Serial}");
        if (disk.LayoutKind == "GPT" && disk.LayoutId != Environment.GetEnvironmentVariable("PRICECHECK_HW_DISK_GUID"))
            throw new InvalidOperationException($"disk GPT GUID mismatch: {disk.LayoutId}");
        if (disk.LayoutKind == "MBR" && disk.LayoutId != Environment.GetEnvironmentVariable("PRICECHECK_HW_DISK_SIGNATURE"))
            throw new InvalidOperationException($"disk MBR signature mismatch: {disk.LayoutId}");
    }
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    if (Environment.GetEnvironmentVariable("PRICECHECK_TEST_BIND") == "1")
    {
        var local = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(value => value.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address));
        if (local is null) throw new InvalidOperationException("No local Ethernet IPv4 address for bound-socket test");
        socket.Bind(new IPEndPoint(local, 0));
    }
    await socket.ConnectAsync(IPAddress.Parse("203.0.113.10"), 443).WaitAsync(TimeSpan.FromSeconds(10));
    var received = new byte[2];
    var count = await socket.ReceiveAsync(received.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    if (count != 2 || Encoding.ASCII.GetString(received) != "OK") throw new InvalidOperationException("proxy tunnel failed");
    Console.WriteLine("agent identity and HTTP CONNECT: OK");
    return;
}

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var child = new ProcessStartInfo(Environment.ProcessPath!)
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
child.ArgumentList.Add("--child");
if (args.Contains("--bound")) child.Environment["PRICECHECK_TEST_BIND"] = "1";
child.Environment["PRICECHECK_AGENT_PATH"] = Path.Combine(AppContext.BaseDirectory, "PriceCheck.ClientAgent.dll");
child.Environment["PRICECHECK_HW_UUID"] = Guid.NewGuid().ToString("D");
child.Environment["PRICECHECK_HW_MACHINE_GUID"] = Guid.NewGuid().ToString("D");
child.Environment["PRICECHECK_HW_BOARD_SERIAL"] = "A1B2C3D4E5F60708";
child.Environment["PRICECHECK_HW_VOLUME_SERIAL"] = "AABBCCDD";
child.Environment["PRICECHECK_HW_MAC"] = "02AABBCCDDEE";
child.Environment["PRICECHECK_HW_SYSTEM_SERIAL"] = "ABCDEF123456";
child.Environment["PRICECHECK_HW_CHASSIS_SERIAL"] = "BCDEF1234567";
child.Environment["PRICECHECK_HW_PROCESSOR_ID"] = "0123456789ABCDEF";
child.Environment["PRICECHECK_HW_PROCESSOR_SERIAL"] = "CDEF12345678";
child.Environment["PRICECHECK_HW_MEMORY_SERIAL"] = "D1234567";
child.Environment["PRICECHECK_HW_DISK_SERIAL"] = "E123456789ABCDEF";
child.Environment["PRICECHECK_HW_PROFILE_GUID"] = Guid.NewGuid().ToString("B");
child.Environment["PRICECHECK_HW_PRODUCT_ID"] = "00330-12345-67890-AAOEM";
child.Environment["PRICECHECK_HW_DISK_GUID"] = Guid.NewGuid().ToString("D");
child.Environment["PRICECHECK_HW_DISK_SIGNATURE"] = "A1B2C3D4";
child.Environment["PRICECHECK_HW_SUS_CLIENT_ID"] = Guid.NewGuid().ToString("D");
child.Environment["PRICECHECK_HW_VIDEO_ID"] = Guid.NewGuid().ToString("B");
child.Environment["PRICECHECK_HW_COMPUTER_NAME"] = "PC-TEST1234";
child.Environment["PRICECHECK_HW_INSTALL_DATE"] = "1600000000";
child.Environment["PRICECHECK_HW_ROUTER_MAC"] = "02AABBCCDDEE";
var targetGateway = NetworkInterface.GetAllNetworkInterfaces()
    .SelectMany(adapter => adapter.GetIPProperties().GatewayAddresses)
    .Select(address => address.Address)
    .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
if (targetGateway is not null) child.Environment["PRICECHECK_HW_ROUTER_IP"] =
    BitConverter.ToUInt32(targetGateway.GetAddressBytes()).ToString();
child.Environment["PRICECHECK_PROXY_HOST"] = "127.0.0.1";
child.Environment["PRICECHECK_PROXY_PORT"] = port.ToString();
child.Environment["PRICECHECK_PROXY_USER"] = "user";
child.Environment["PRICECHECK_PROXY_PASS"] = "pass";
using var process = Process.Start(child) ?? throw new InvalidOperationException("child did not start");
var acceptTask = listener.AcceptTcpClientAsync();
var finished = await Task.WhenAny(acceptTask, process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(20)));
if (finished != acceptTask)
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
    throw new InvalidOperationException($"no proxy connection; child exit {process.ExitCode}: {await process.StandardError.ReadToEndAsync()}");
}
using var accepted = await acceptTask;
using var stream = accepted.GetStream();
var request = new List<byte>();
var chunk = new byte[1024];
while (!Encoding.ASCII.GetString(request.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal))
{
    var read = await stream.ReadAsync(chunk.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    if (read == 0) throw new IOException("proxy request ended early");
    request.AddRange(chunk.AsSpan(0, read).ToArray());
    if (request.Count > 16384) throw new IOException("proxy request too long");
}
var text = Encoding.ASCII.GetString(request.ToArray());
if (!text.StartsWith("CONNECT 203.0.113.10:443 HTTP/1.1\r\n", StringComparison.Ordinal) ||
    !text.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n", StringComparison.Ordinal))
    throw new InvalidOperationException($"unexpected CONNECT request: {text}");
await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\nOK"));
await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
var output = await process.StandardOutput.ReadToEndAsync();
var error = await process.StandardError.ReadToEndAsync();
Console.Write(output);
if (process.ExitCode != 0) throw new InvalidOperationException($"child exited {process.ExitCode}: {error}");
listener.Stop();

static string ReadSmbiosString(byte[] table, int offset, int length, byte index)
{
    if (index == 0) return "";
    var position = offset + length;
    for (var current = 1; current < index && position < table.Length; current++)
    {
        while (position < table.Length && table[position] != 0) position++;
        position++;
    }
    var end = position;
    while (end < table.Length && table[end] != 0) end++;
    return end > position && end < table.Length ? Encoding.Latin1.GetString(table, position, end - position) : "";
}

internal static class Native
{
    public static (string Serial, string LayoutKind, string LayoutId)? ReadDiskIdentity(int index)
    {
        using var handle = CreateFileW($@"\\.\PhysicalDrive{index}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var descriptor = new byte[4096];
        var query = new byte[12];
        if (!DeviceIoControl(handle, 0x002D1400, query, query.Length, descriptor, descriptor.Length,
                out var returned, IntPtr.Zero) || returned < 28) return null;
        var offset = (int)BitConverter.ToUInt32(descriptor, 24);
        if (offset < 28 || offset >= returned) return null;
        var end = offset;
        while (end < returned && descriptor[end] != 0) end++;
        var serial = Encoding.ASCII.GetString(descriptor, offset, end - offset);
        var layout = new byte[4096];
        if (!DeviceIoControl(handle, 0x00070050, Array.Empty<byte>(), 0, layout, layout.Length,
                out returned, IntPtr.Zero) || returned < 24) return null;
        var style = BitConverter.ToUInt32(layout, 0);
        return style switch
        {
            0 => (serial, "MBR", BitConverter.ToUInt32(layout, 8).ToString("X8")),
            1 => (serial, "GPT", new Guid(layout.AsSpan(8, 16)).ToString("D")),
            _ => null
        };
    }

    public static string? ReadMachineGuid()
    {
        return ReadRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
    }

    public static string? ReadRegistryValue(string path, string name)
    {
        if (RegOpenKeyExW(new IntPtr(unchecked((int)0x80000002)), path, 0, 0x20019, out var key) != 0)
            throw new InvalidOperationException($"cannot open {name} key");
        try
        {
            var data = new byte[256];
            uint size = (uint)data.Length;
            if (RegQueryValueExW(key, name, IntPtr.Zero, out _, data, ref size) != 0)
                throw new InvalidOperationException($"cannot read {name}");
            return Encoding.Unicode.GetString(data, 0, checked((int)size)).TrimEnd('\0');
        }
        finally { RegCloseKey(key); }
    }

    public static uint ReadRegistryDword(string path, string name)
    {
        if (RegOpenKeyExW(new IntPtr(unchecked((int)0x80000002)), path, 0, 0x20019, out var key) != 0)
            throw new InvalidOperationException($"cannot open {name} key");
        try
        {
            var data = new byte[4];
            uint size = 4;
            if (RegQueryValueExW(key, name, IntPtr.Zero, out _, data, ref size) != 0 || size != 4)
                throw new InvalidOperationException($"cannot read {name}");
            return BitConverter.ToUInt32(data);
        }
        finally { RegCloseKey(key); }
    }

    public static string? ReadRouterMac(IPAddress gateway)
    {
        var data = new byte[8];
        var length = data.Length;
        if (SendARP(BitConverter.ToUInt32(gateway.GetAddressBytes()), 0, data, ref length) != 0 || length < 6)
            return null;
        return Convert.ToHexString(data, 0, 6);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryW(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetVolumeInformationW(string root, StringBuilder? name, int nameSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder? fileSystem, int fileSystemSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetSystemFirmwareTable(uint provider, uint table, [Out] byte[]? buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SendARP(uint destination, uint source, byte[] mac, ref int length);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyExW(IntPtr root, string subkey, uint options, uint access, out IntPtr key);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueExW(IntPtr key, string name, IntPtr reserved, out uint type, byte[] data, ref uint size);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr key);
}
