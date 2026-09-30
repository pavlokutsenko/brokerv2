using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;

namespace PriceCheck.Collector.Runtime.Driver;

public sealed record ProcessRegistryIdentity(string ProductId, string SusClientId, string SqmMachineId,
    string VideoIdentifier, string ComputerName, string ProcessorModel, string ProcessorRevision, uint InstallDate);

public sealed partial class Lu4Device
{
    // Returns false only for an older driver without this additive IOCTL.
    public bool SetProcessIdentity(int pid, long createdFileTime, Guid profileId,
        string machineGuid, string hardwareProfileGuid, string systemUuid, ProcessRegistryIdentity additional)
    {
        var request = ProcessIdentityRequest(pid, createdFileTime, profileId, machineGuid, hardwareProfileGuid, systemUuid, additional);
        byte[] response;
        try { response = Ioctl(Code(0x812, WriteAccess), request, request.Length); }
        catch (Win32Exception error) when (error.NativeErrorCode is 1 or 50) { return false; }
        Validate(response, request.Length);
        if (BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != pid ||
            BinaryPrimitives.ReadInt64LittleEndian(response.AsSpan(8)) != createdFileTime ||
            new Guid(response.AsSpan(16, 16)) != profileId ||
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(272)) != 15)
            throw new IOException("LU4Memory identity response does not match the client/profile.");
        return true;
    }

    internal static byte[] ProcessIdentityRequest(int pid, long createdFileTime, Guid profileId,
        string machineGuid, string hardwareProfileGuid, string systemUuid, ProcessRegistryIdentity additional)
    {
        if (pid <= 0 || createdFileTime <= 0 || profileId == Guid.Empty ||
            !Guid.TryParse(machineGuid, out _) || !Guid.TryParse(hardwareProfileGuid, out _) ||
            !Guid.TryParse(systemUuid, out var uuid) || machineGuid.Length >= 40 || hardwareProfileGuid.Length >= 40)
            throw new ArgumentException("Invalid process identity configuration.");
        if (!Guid.TryParse(additional.SusClientId, out _) || !Guid.TryParse(additional.SqmMachineId, out _) ||
            !Guid.TryParse(additional.VideoIdentifier, out _) || additional.ProcessorRevision.Length != 16 ||
            !additional.ProcessorRevision.All(Uri.IsHexDigit) || additional.InstallDate == 0)
            throw new ArgumentException("Invalid additional registry identity.");
        var request = new byte[1048];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), pid);
        BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(8), createdFileTime);
        profileId.ToByteArray().CopyTo(request, 16);
        Encoding.Unicode.GetBytes(machineGuid).CopyTo(request, 32);
        Encoding.Unicode.GetBytes(hardwareProfileGuid).CopyTo(request, 112);
        Encoding.Unicode.GetBytes(uuid.ToString("D")).CopyTo(request, 192);
        WriteText(request, 280, 64, additional.ProductId);
        WriteText(request, 408, 40, additional.SusClientId);
        WriteText(request, 488, 40, additional.SqmMachineId);
        WriteText(request, 568, 40, additional.VideoIdentifier);
        WriteText(request, 648, 64, additional.ComputerName);
        WriteText(request, 776, 128, additional.ProcessorModel);
        Convert.FromHexString(additional.ProcessorRevision).CopyTo(request, 1032);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(1040), additional.InstallDate);
        return request;
    }

    private static void WriteText(byte[] request, int offset, int chars, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length >= chars || value.Any(ch => ch < 32 || ch > 126))
            throw new ArgumentException("Invalid registry identity text.");
        Encoding.Unicode.GetBytes(value).CopyTo(request, offset);
    }
}
