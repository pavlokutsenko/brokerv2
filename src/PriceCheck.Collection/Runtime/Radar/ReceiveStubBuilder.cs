using System.Buffers.Binary;

namespace PriceCheck.Collector.Runtime.Radar;

internal static class ReceiveStubBuilder
{
    private static readonly byte[] Template = Convert.FromHexString(
        "9C50535152565741504151415241534D89F04189F1" +
        "4180383175704181F9000400007767909090" +
        "48B800000000000000008B3889FAFFC281FA3F000000720231D2" +
        "49BA0000000000000000413B12743541BA04040000490FAFFA" +
        "49BB00000000000000004C01DF44890F4883C7044C89C64C89C9FCF3A4" +
        "48B800000000000000008910EB06FF0500000000" +
        "415B415A415941585F5E5A595B589D" +
        "410FB61EB80100000080FBFE41BF03000000FF25000000000000000000000000");

    public static byte[] Build(ulong cave, ulong ring, ulong hook, ulong dropCounter)
    {
        var stub = Template.ToArray();
        Write64(stub, 0x29, ring);
        Write32(stub, 0x39, ReceiveHookSession.Capacity);
        Write64(stub, 0x43, ring + 4);
        Write32(stub, 0x52, ReceiveHookSession.SlotSize);
        Write64(stub, 0x5C, ring + 8);
        Write64(stub, 0x79, ring);
        var stubAddress = cave + 0x340;
        Write32(stub, 0x87, checked((int)((long)dropCounter - (long)(stubAddress + 0x8B))));
        Write64(stub, 0xB2, hook + 18);

        // Route the old validation block to an opcode filter appended after
        // the absolute return pointer. Saved RAX makes AL safe scratch space.
        stub[0x15] = 0xE9;
        Write32(stub, 0x16, Template.Length - 0x1A);
        Array.Fill(stub, (byte)0x90, 0x1A, 0x27 - 0x1A);
        var tail = new List<byte>();
        tail.AddRange([0x45, 0x85, 0xC9]);                    // test r9d,r9d
        var invalidLength = EmitNearJump(tail, 0x0F, 0x8E); // jle cleanup
        tail.AddRange([0x41, 0x81, 0xF9]); tail.AddRange(BitConverter.GetBytes(ReceiveHookSession.MaxPacket));
        var tooLong = EmitNearJump(tail, 0x0F, 0x87);       // ja cleanup
        tail.AddRange([0x41, 0x8A, 0x00]);                  // mov al,[r8]
        var accepts = new List<int>();
        foreach (var opcode in new byte[] { 0x31, 0x2F, 0x47, 0x08, 0x32 })
        {
            tail.AddRange([0x3C, opcode]);
            accepts.Add(EmitNearJump(tail, 0x0F, 0x84));
        }
        var reject = EmitNearJump(tail, 0xE9);
        var acceptOffset = Template.Length + tail.Count;
        var accept = EmitNearJump(tail, 0xE9);
        var cleanupOffset = 0x8B;
        Patch(stub, tail, invalidLength, cleanupOffset);
        Patch(stub, tail, tooLong, cleanupOffset);
        Patch(stub, tail, reject, cleanupOffset);
        foreach (var jump in accepts) Patch(stub, tail, jump, acceptOffset);
        Patch(stub, tail, accept, 0x27);
        return [.. stub, .. tail];
    }

    private static int EmitNearJump(List<byte> bytes, params byte[] opcode)
    {
        bytes.AddRange(opcode);
        var displacement = bytes.Count;
        bytes.AddRange([0, 0, 0, 0]);
        return displacement;
    }

    private static void Patch(byte[] prefix, List<byte> tail, int displacementOffset, int targetOffset)
    {
        var instructionEnd = prefix.Length + displacementOffset + 4;
        var value = targetOffset - instructionEnd;
        var encoded = BitConverter.GetBytes(value);
        for (var index = 0; index < 4; index++) tail[displacementOffset + index] = encoded[index];
    }

    private static void Write32(byte[] value, int offset, int data) =>
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(offset), data);
    private static void Write64(byte[] value, int offset, ulong data) =>
        BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(offset), data);
}
