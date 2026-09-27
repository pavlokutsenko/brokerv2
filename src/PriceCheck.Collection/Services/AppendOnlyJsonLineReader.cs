using System.Text;

namespace PriceCheck.Collector.Services;

// Advance only after a complete newline-terminated UTF-8 record was consumed.
// A failed consumer is retried; an incomplete final record stays on disk.
public sealed class AppendOnlyJsonLineReader(string path)
{
    private long _offset;
    public long Offset => _offset;
    public long BytesRead { get; private set; }
    public void VisitNewLines(Action<string> consume,CancellationToken cancellationToken=default)
    {
        if(!File.Exists(path))return;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(file.Length<_offset)_offset=0;
        if(file.Length==_offset)return;
        file.Position=_offset;
        using var line=new MemoryStream();
        var buffer=new byte[8192];var position=_offset;
        int count;
        while((count=file.Read(buffer,0,buffer.Length))>0)
        {
            cancellationToken.ThrowIfCancellationRequested();BytesRead+=count;
            var start=0;
            for(var index=0;index<count;index++)
            {
                if(buffer[index]!=10)continue;
                line.Write(buffer,start,index-start);
                var length=(int)line.Length;
                if(length>0&&line.GetBuffer()[length-1]==13)length--;
                consume(Encoding.UTF8.GetString(line.GetBuffer(),0,length));
                _offset=position+index+1;
                line.SetLength(0);start=index+1;
            }
            line.Write(buffer,start,count-start);position+=count;
            if(line.Length>4*1024*1024)throw new InvalidDataException("Native event exceeds the 4 MiB record limit.");
        }
    }
}
