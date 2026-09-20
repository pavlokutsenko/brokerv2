using System.Buffers.Binary;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class PacketRingReader
{
    private readonly Lu4Device _device;
    private readonly int _pid;
    private readonly ulong _ring;
    private readonly ulong _dropCounter;

    public PacketRingReader(Lu4Device device, int pid, ulong ring, ulong dropCounter) =>
        (_device, _pid, _ring, _dropCounter) = (device, pid, ring, dropCounter);

    public async Task RunAsync(Action<ReadOnlyMemory<byte>> onPacket, CancellationToken cancellationToken)
    {
        var polls = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (++polls % 100 == 0 && BinaryPrimitives.ReadInt32LittleEndian(_device.Read(_pid, _dropCounter, 4)) != 0)
                throw new InvalidDataException("Packet ring переполнен; полный радар не гарантирован");
            // One driver round-trip snapshots the complete bounded ring. Reading
            // every slot separately was too slow during a dense area transition
            // and let the producer lap the consumer.
            var snapshot = _device.Read(_pid, _ring, ReceiveHookSession.RingSize);
            var write = BinaryPrimitives.ReadInt32LittleEndian(snapshot);
            var read = BinaryPrimitives.ReadInt32LittleEndian(snapshot.AsSpan(4));
            if ((uint)write >= ReceiveHookSession.Capacity || (uint)read >= ReceiveHookSession.Capacity)
                throw new InvalidDataException($"Некорректные индексы packet ring: {write}/{read}");
            while (read != write)
            {
                var offset = 8 + read * ReceiveHookSession.SlotSize;
                var slot = snapshot.AsMemory(offset, ReceiveHookSession.SlotSize);
                var length = BinaryPrimitives.ReadInt32LittleEndian(slot.Span);
                if (length <= 0 || length > ReceiveHookSession.MaxPacket)
                    throw new InvalidDataException($"Некорректная длина packet ring: {length}");
                onPacket(slot.Slice(4, length));
                read = (read + 1) % ReceiveHookSession.Capacity;
            }
            var next = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(next, read);
            _device.Write(_pid, _ring + 4, next);
            await Task.Delay(1, cancellationToken);
        }
    }
}
