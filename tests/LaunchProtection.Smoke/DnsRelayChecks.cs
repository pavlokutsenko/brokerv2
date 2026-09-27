using System.Buffers.Binary;
using PriceCheck.Collector.Services;

internal static class DnsRelayChecks
{
    public static async Task RunAsync()
    {
        var first = Query(0x1234); var second = Query(0x5678);
        using var stream = new Duplex(Frame(first).Concat(Frame(second)).ToArray());
        var calls = 0;
        await ProxyTcpBroker.RelayDnsAsync(stream, (query, _) =>
        {
            if (!query.SequenceEqual(calls++ == 0 ? first : second)) throw new Exception("DNS frame boundaries changed.");
            var response = query.ToArray(); response[2] |= 0x80;
            return Task.FromResult(response);
        }, default);
        first[2] |= 0x80; second[2] |= 0x80;
        if (calls != 2 || !stream.Output.ToArray().SequenceEqual(Frame(first).Concat(Frame(second))))
            throw new Exception("DNS response framing changed.");
        foreach (var kind in new[] { "short-query", "wrong-id", "not-response", "too-large", "https-failure" })
        {
            var input = kind == "short-query" ? Frame(new byte[11]) : Frame(Query(1));
            using var invalid = new Duplex(input);
            try
            {
                await ProxyTcpBroker.RelayDnsAsync(invalid, (query, _) =>
                {
                    if (kind == "short-query") throw new Exception("Malformed DNS request reached the resolver.");
                    if (kind == "https-failure") throw new IOException("HTTPS failed");
                    var response = kind == "too-large" ? new byte[65536] : query.ToArray();
                    if (kind != "not-response") response[2] |= 0x80;
                    if (kind == "wrong-id") response[0] ^= 1;
                    return Task.FromResult(response);
                }, default);
                throw new Exception("Invalid DNS exchange accepted: " + kind);
            }
            catch (IOException) { }
            if (invalid.Output.Length != 0) throw new Exception("Invalid DNS response reached the client.");
        }
        Console.WriteLine("DNS relay: fragmented frames, multiple queries and fail-closed errors PASS");
    }
    private static byte[] Query(ushort id) { var query = new byte[12]; BinaryPrimitives.WriteUInt16BigEndian(query, id); return query; }
    private static byte[] Frame(byte[] payload) { var result = new byte[payload.Length + 2]; BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)payload.Length); payload.CopyTo(result, 2); return result; }
    private sealed class Duplex(byte[] input) : Stream
    {
        private readonly MemoryStream _input = new(input);
        public MemoryStream Output { get; } = new();
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, Math.Min(1, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _input.ReadAsync(buffer[..Math.Min(1, buffer.Length)], token);
        public override void Write(byte[] buffer, int offset, int count) => Output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) => Output.WriteAsync(buffer, token);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { _input.Dispose(); Output.Dispose(); } base.Dispose(disposing); }
    }
}
