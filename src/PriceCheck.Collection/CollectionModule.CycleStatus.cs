using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private readonly Dictionary<Guid,DateTimeOffset> _statusPublished = [];
    private readonly Dictionary<Guid,string> _statusPhases = [];

    private async Task PublishCycleStatusAsync(ProfileRuntime runtime)
    {
        var now=DateTimeOffset.UtcNow;var id=runtime.Profile.Id;
        if(_statusPublished.TryGetValue(id,out var last) && now-last<TimeSpan.FromSeconds(30) &&
           _statusPhases.GetValueOrDefault(id)==runtime.Cycle.Phase) return;
        _statusPublished[id]=now;_statusPhases[id]=runtime.Cycle.Phase;
        try
        {
            if(_cycles.TryGetValue(id,out var cycle)) await _uploadOutbox.EnqueueCycleStatusAsync(cycle.UploadProfile,runtime.Cycle,cycle.WorkerId);
            var dir=_uploadOutbox.OutputDirectory;
            var files=Directory.Exists(dir)?Directory.GetFiles(dir):[];
            var waiting=files.Count(p=>p.EndsWith(".ready") || Path.GetFileName(p).Contains(".sending."));
            var rejected=Path.Combine(dir,"rejected");
            var errors=Directory.Exists(rejected)?Directory.GetFiles(rejected,"*.json").Length:0;
            runtime.UploadStatus=_localStores.TryGetValue(id,out var local)
                ?$"Local outbox · {local.PendingUploads} individual operations pending"
                :$"Uploads across profiles: {waiting} waiting · {errors} rejected";
            if(waiting>0) _startUploadWorker();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        { runtime.UploadStatus=$"Server queue: {e.Message}"; }
    }
}
