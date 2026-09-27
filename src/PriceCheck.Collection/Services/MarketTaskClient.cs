using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class MarketTaskClient : IMarketTaskClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<MarketCycleStatus> StateAsync(CollectorProfile p) => Post<MarketCycleStatus>(p,"state",new {market=p.Name,city=p.City});
    public Task<MarketTaskClaim> ClaimAsync(CollectorProfile p,string workerId,string batchId,string requestId,
        IReadOnlyList<string> keys,double x,double y) => Post<MarketTaskClaim>(p,"claim",new {
            market=p.Name,city=p.City,workerId,batchId,requestId,traderKeys=keys,currentX=x,currentY=y,limit=500 });
    public async Task<IReadOnlyList<string>> RenewAsync(CollectorProfile p,string workerId,IReadOnlyList<ServerPriceJob> jobs)
    {
        var result=await Post<Renewed>(p,"renew",new {market=p.Name,city=p.City,workerId,
            jobs=jobs.Select(j=>new {j.TraderId,j.LeaseToken,j.Revision})});
        return result.Valid;
    }
    public async Task ReleaseAsync(CollectorProfile p,string workerId,IReadOnlyList<MarketTaskRelease> jobs)
    {
        if(jobs.Count==0) return;
        await Post<JsonElement>(p,"release",new {market=p.Name,city=p.City,workerId,
            jobs=jobs.Select(j=>new {j.Job.TraderId,j.Job.LeaseToken,j.Job.Revision,error=j.Error})});
    }
    private static async Task<T> Post<T>(CollectorProfile p,string operation,object body)
    {
        using var response=await Http.PostAsJsonAsync($"{p.ServerUrl.TrimEnd('/')}/v2/price-check-queue/{operation}",body,Json);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new InvalidDataException("Server returned no queue data.");
    }
    private sealed record Renewed(string[] Valid);
}
