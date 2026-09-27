using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PriceCheck.Collector.Runtime.Driver;

if(args.Length==2 && args[0]=="fixture")
{
    var bytes=Enumerable.Repeat(byte.Parse(args[1]),256).ToArray();
    var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);
    try { Console.WriteLine(JsonSerializer.Serialize(new {pid=Environment.ProcessId,address=(ulong)handle.AddrOfPinnedObject(),value=bytes[0]})); Console.ReadLine(); }
    finally { handle.Free(); }
    return;
}
var processes=new List<Process>();
try
{
    var fixtures=new List<(int Pid,ulong Address,byte Value)>();
    foreach(var value in new[]{37,211})
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        start.ArgumentList.Add("fixture");start.ArgumentList.Add(value.ToString());
        var process=Process.Start(start)!;processes.Add(process);
        using var d=JsonDocument.Parse((await process.StandardOutput.ReadLineAsync())!);
        fixtures.Add((d.RootElement.GetProperty("pid").GetInt32(),d.RootElement.GetProperty("address").GetUInt64(),(byte)value));
    }
    await Task.WhenAll(Enumerable.Range(0,8).Select(index=>Task.Run(()=>{
        using var device=new Lu4Device();
        for(var n=0;n<500;n++)
        {
            var f=fixtures[(index+n)%fixtures.Count];
            var bytes=device.Read(f.Pid,f.Address,256);
            if(bytes.Any(b=>b!=f.Value)) throw new Exception("Cross-PID driver response or corrupted bytes.");
        }
    })));
    // A closed handle must not invalidate another window's connection.
    using var remaining=new Lu4Device();
    if(remaining.Read(fixtures[1].Pid,fixtures[1].Address,1)[0]!=211) throw new Exception("Handle lifecycle isolation failed.");
    Console.WriteLine("DRIVER_CONCURRENCY_OK 2 synthetic PIDs, 8 handles, 4000 interleaved reads; no game writes");
}
finally
{
    foreach(var process in processes) { await process.StandardInput.WriteLineAsync(); await process.WaitForExitAsync(); process.Dispose(); }
}
