using System.Net.Sockets;
using System.Reflection;
using PriceCheck.Collector.Models;
using PriceCheck.Launching;

internal static class ProxyRelayTests
{
    public static async Task Run()
    {
        var type=typeof(LaunchModule).Assembly.GetType("PriceCheck.Collector.Services.ProxyTcpBroker")!;
        foreach(var direction in new[] {"client", "upstream", "eof"})
        {
            using var broker=(IDisposable)Activator.CreateInstance(type, [new LaunchTemplate {ProxyEnabled=false}, true])!;
            using var game=direction=="client" ? new ResetStream() : new MemoryStream();
            using var network=direction=="upstream" ? new ResetStream() : new MemoryStream();
            using var session=new CancellationTokenSource();
            var closed=false;
            await (Task)type.GetMethod("RelayAsync", BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(broker, [game,network,7782,session,new Action(()=>closed=true)])!;
            if(!closed || !session.IsCancellationRequested || type.GetProperty("Error")!.GetValue(broker) is not null)
                throw new Exception("Tunnel termination revoked protection or failed to drain both pumps.");
            var diagnostic=(string?)type.GetProperty("NetworkError")!.GetValue(broker);
            if(direction!="eof" && (diagnostic is null || !diagnostic.Contains("ConnectionReset") || !diagnostic.Contains("7782")))
                throw new Exception("Tunnel reset did not retain its non-sensitive socket diagnostic.");
            if(direction=="eof" && diagnostic is not null)
                throw new Exception("Normal EOF was reported as a socket fault.");
        }
        Console.WriteLine("PROXY_RELAY_OK client_reset upstream_reset eof drain no_protection_failure");
    }

    private sealed class ResetStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default) =>
            ValueTask.FromException<int>(new IOException("Fixture reset",new SocketException((int)SocketError.ConnectionReset)));
    }
}
