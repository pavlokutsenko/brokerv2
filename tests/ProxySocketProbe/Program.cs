using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1 || !int.TryParse(args[0], out var pid) || pid <= 0)
    throw new ArgumentException("Pass an active target PID");

Console.WriteLine("Enter proxy host, port, login and password on four lines; console echo is disabled.");
var handle = GetStdHandle(-10);
var hasConsole = GetConsoleMode(handle, out var oldMode);
if (hasConsole) SetConsoleMode(handle, oldMode & ~4u);
string host, user, password;
int port;
try
{
    host = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
    port = int.Parse(await Console.In.ReadLineAsync() ?? throw new EndOfStreamException());
    user = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
    password = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
}
finally { if (hasConsole) SetConsoleMode(handle, oldMode); }

using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
await socket.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(10));
var endpoint = "194.180.209.45:2108";
var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
var request = Encoding.ASCII.GetBytes($"CONNECT {endpoint} HTTP/1.1\r\nHost: {endpoint}\r\nProxy-Authorization: Basic {authorization}\r\n\r\n");
await socket.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(10));
var response = new byte[4096];
var length = await socket.ReceiveAsync(response).WaitAsync(TimeSpan.FromSeconds(10));
var line = Encoding.ASCII.GetString(response, 0, length).Split("\r\n", 2)[0];
if (!line.Contains(" 200 ", StringComparison.Ordinal))
    throw new InvalidOperationException($"Proxy tunnel failed: {line}");
Console.WriteLine("Proxy tunnel: 200");
try
{
    var transfer = socket.DuplicateAndClose(pid);
    Console.WriteLine($"WSADuplicateSocket: OK, protocol bytes={transfer.ProtocolInformation.Length}, flags={transfer.Options}");
}
catch (Exception exception)
{
    var socketError = exception is SocketException socketException ? socketException.SocketErrorCode.ToString() : "n/a";
    Console.WriteLine($"WSADuplicateSocket: {exception.GetType().Name}, socket error={socketError}");
    Environment.ExitCode = 1;
}

[DllImport("kernel32.dll")] static extern nint GetStdHandle(int kind);
[DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetConsoleMode(nint handle, out uint mode);
[DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetConsoleMode(nint handle, uint mode);
