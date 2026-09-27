using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Services;

internal static class TailReaderTests
{
    public static void Run(string root)
    {
        var file=Path.Combine(root,"native-tail.jsonl");var lines=new List<string>();
        var bytes=Encoding.UTF8.GetBytes("{\"name\":\"Лавка\",\"snapshotId\":\"stable-1\"}\n");
        var split=Array.IndexOf(bytes,(byte)0xd0)+1; // Between the bytes of one Cyrillic character.
        File.WriteAllBytes(file,bytes[..split]);var tail=new AppendOnlyJsonLineReader(file);
        tail.VisitNewLines(lines.Add);
        Require(lines.Count==0&&tail.Offset==0,"partial UTF8/event line remains unpublished");
        using(var append=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))append.Write(bytes[split..]);
        tail.VisitNewLines(lines.Add);
        using(var eventJson=JsonDocument.Parse(lines.Single()))Require(eventJson.RootElement.GetProperty("name").GetString()=="Лавка","UTF8 split reconnects without replacement characters");
        var read=tail.BytesRead;tail.VisitNewLines(lines.Add);
        Require(tail.BytesRead==read&&lines.Count==1,"unchanged event file is neither reread nor reparsed");
        File.AppendAllText(file,"{\"snapshotId\":\"stable-2\"}\n",new UTF8Encoding(false));
        var fail=true;
        try{tail.VisitNewLines(line=>{if(fail){fail=false;throw new IOException("durable store unavailable");}});}catch(IOException){}
        tail.VisitNewLines(lines.Add);
        Require(lines.Count==2&&lines[1].Contains("stable-2"),"failed persistence consumer retries same complete stable event");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        File.AppendAllText(file,"{\"snapshotId\":\"stable-3\"}\n",new UTF8Encoding(false));
        try{tail.VisitNewLines(lines.Add,cancelled.Token);}catch(OperationCanceledException){}
        tail.VisitNewLines(lines.Add);
        Require(lines.Count==3&&lines[2].Contains("stable-3"),"cancellation preserves unread final event for recovery");
        Console.WriteLine("INCREMENTAL TAIL PASS 5 checks");
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
