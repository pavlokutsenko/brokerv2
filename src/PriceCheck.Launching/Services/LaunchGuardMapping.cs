using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace PriceCheck.Collector.Services;

internal sealed class LaunchGuardMapping : IDisposable
{
    public string Name { get; } = @"Local\PriceCheckLaunchGuard_" + Guid.NewGuid().ToString("N");
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _world;
    public string IdentityTag { get; }
    public LaunchGuardMapping(string world)
    {
        _world = Convert.FromHexString(world);
        if (_world.Length != 16) throw new LaunchProtectionException("Неверная идентичность HWID.");
        IdentityTag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_world))[..12];
        _mapping = MemoryMappedFile.CreateNew(Name, 104);
        _view = _mapping.CreateViewAccessor();
        _view.Write(0, 0x50434744u); _view.Write(4, 1u);
        _view.WriteArray(48, _world, 0, 16);
        _view.Write(80, 3); // Both HWID and proxy are required.
        Heartbeat();
    }
    public void Bind(int pid)
    {
        var session = PriceCheck.Windows.ClientProcessIdentity.Read(pid)
            ?? throw new LaunchProtectionException("Драйвер не подтвердил время создания игрового PID.");
        _view.Write(64, session.StartedAtUtc.UtcDateTime.ToFileTimeUtc());
        _view.Write(8, pid);
    }
    public void Heartbeat() => _view.Write(16, Environment.TickCount64);
    public void ControllerReady(bool ready) => _view.Write(84, ready ? 1 : 0);
    public (bool Hardware, bool World, long Tick, int WorldCount, int Error) Read()
    {
        var flags = _view.ReadInt32(24);
        var applied = new byte[16]; _view.ReadArray(32, applied, 0, 16);
        var count = _view.ReadInt32(28);
        var world = (flags & 2) != 0 && applied.SequenceEqual(_world) && count > 0 &&
            _view.ReadInt64(96) > 0 && _view.ReadInt64(88) >= _view.ReadInt64(96);
        return ((flags & 1) != 0, world, _view.ReadInt64(72), count, _view.ReadInt32(12));
    }
    public void Dispose() { _view.Dispose(); _mapping.Dispose(); }
}
