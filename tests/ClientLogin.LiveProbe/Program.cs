using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using PriceCheck.Collector.Services;

if (args.Length is < 2 or > 3 || !int.TryParse(args[0], out var pid) || !File.Exists(args[1]) ||
    (args.Length == 3 && args[2] != "--character"))
    throw new ArgumentException("Pass LU4 PID, native probe DLL path and optional --character");
var characterMode = args.Length == 3;
string user = "", password = "";
if (!characterMode)
{
    Console.WriteLine("Enter test account login and password on two lines; console echo is disabled.");
    var inputHandle = ConsoleInput.GetStdHandle(-10);
    var hadConsole = ConsoleInput.GetConsoleMode(inputHandle, out var originalMode);
    if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode & ~4u);
    try
    {
        user = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
        password = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
    }
    finally
    {
        if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode);
    }
    if (user.Length is < 1 or > 120 || password.Length is < 1 or > 120)
        throw new ArgumentOutOfRangeException("Account fields must contain 1-120 characters");
}

using var mapping = MemoryMappedFile.CreateNew($@"Local\PriceCheckLoginProbe_{pid}", 4096,
    MemoryMappedFileAccess.ReadWrite);
using var view = mapping.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.ReadWrite);
view.Write(0, 0x50434C47u);
view.Write(4, (ushort)user.Length);
view.Write(6, (ushort)password.Length);
view.Write(8, 0);
view.Write(12, characterMode ? -1 : 1);
for (var i = 0; i < user.Length; i++) view.Write(16 + i * 2, user[i]);
view.Write(16 + user.Length * 2, (char)0);
var passwordOffset = 16 + (user.Length + 1) * 2;
for (var i = 0; i < password.Length; i++) view.Write(passwordOffset + i * 2, password[i]);
view.Write(passwordOffset + password.Length * 2, (char)0);

using var hook = await ClientAgentHookLoader.InstallAsync(pid, args[1], CancellationToken.None);
var deadline = DateTime.UtcNow.AddSeconds(20);
while (DateTime.UtcNow < deadline)
{
    var status = view.ReadInt32(8);
    if (status < 0 || status == (characterMode ? 4 : 3))
    {
        Console.WriteLine($"Native login call status: {status}");
        break;
    }
    hook.Pulse();
    await Task.Delay(100);
}
Console.WriteLine($"Final status: {view.ReadInt32(8)}");
view.Write(0, 0u);
for (var i = 16; i < 16 + 2 * (user.Length + password.Length + 2); i++) view.Write(i, (byte)0);

internal static class ConsoleInput
{
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetConsoleMode(nint handle, uint mode);
}
