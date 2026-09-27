using PriceCheck.Collector.Models;
using PriceCheck.Contracts;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private Func<ClientSession,CancellationToken,Task> CaptureReaderBeforeLogin(ProfileRuntime runtime) =>
        async (session,token)=>
        {
            runtime.Session=session;
            await _collection.AttachAsync(runtime,token);
            Log($"{runtime.Profile.Name}: recording character identities before world entry · PID {session.ProcessId}");
        };
}
