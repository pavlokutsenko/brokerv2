using System.Buffers.Binary;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class ReceiveHookSession : IDisposable
{
    public const int MaxPacket = 0x200;
    public const int SlotSize = MaxPacket + 4;
    public const int Capacity = 126;
    public const int RingSize = 0x10000;
    private static readonly byte[] Original = Convert.FromHexString("410FB61EB80100000080FBFE41BF03000000");
    private readonly Lu4Device _device;
    private readonly int _pid;
    private ulong _hook;
    private ulong _cave;
    private ulong _ring;
    private uint _oldProtection;
    private bool _installed;

    private ReceiveHookSession(Lu4Device device, int pid) { _device = device; _pid = pid; }
    public ulong RingAddress => _ring;
    public ulong DropCounterAddress => _cave + 0x110;

    public static ReceiveHookSession Install(Lu4Device device, int pid)
    {
        var session = new ReceiveHookSession(device, pid);
        try { session.InstallCore(); return session; }
        catch { session.Dispose(); throw; }
    }

    private void InstallCore()
    {
        var imageBase = _device.GetProcessBase(_pid);
        var signature = HookSignatureScanner.FindUnique(_device, _pid, imageBase);
        _hook = signature + 0x11;
        if (!_device.Read(_pid, _hook, Original.Length).SequenceEqual(Original))
            throw new InvalidOperationException("Исходные байты receive-hook не совпали");

        _cave = _device.Allocate(_pid, 0x1000);
        _ring = _device.Allocate(_pid, RingSize);
        _device.Write(_pid, _cave, new byte[0x1000]);
        _device.Write(_pid, _ring, new byte[RingSize]);
        var stub = ReceiveStubBuilder.Build(_cave, _ring, _hook, _cave + 0x110);
        _device.Write(_pid, _cave + 0x340, stub);
        _oldProtection = _device.Protect(_pid, _hook, (ulong)Original.Length, 0x40);
        var patch = new byte[Original.Length];
        patch[0] = 0xFF; patch[1] = 0x25;
        BinaryPrimitives.WriteUInt64LittleEndian(patch.AsSpan(6), _cave + 0x340);
        patch[14] = 0x90; patch[15] = 0x90; patch[16] = 0x90; patch[17] = 0x90;
        _device.Write(_pid, _hook, patch);
        _device.Protect(_pid, _hook, (ulong)Original.Length, _oldProtection);
        _installed = true;
    }

    public void Dispose()
    {
        if (_hook != 0 && _installed)
        {
            try
            {
                _device.Protect(_pid, _hook, (ulong)Original.Length, 0x40);
                _device.Write(_pid, _hook, Original);
                _device.Protect(_pid, _hook, (ulong)Original.Length, _oldProtection);
            }
            catch { }
            _installed = false;
        }
        if (_ring != 0) { try { _device.Free(_pid, _ring); } catch { } _ring = 0; }
        if (_cave != 0) { try { _device.Free(_pid, _cave); } catch { } _cave = 0; }
    }
}
