using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public static class ClientResourceBudget
{
    public static void Validate(LaunchTemplate template)
    {
        if (template.MemoryBudgetEnabled && template.MemoryBudgetMiB is < 256 or > 65536)
            throw new ArgumentException("Лимит рабочего набора должен быть от 256 до 65536 МиБ.");
        if (template.CpuBudgetEnabled && template.CpuBudgetPercent is < 1 or > 100)
            throw new ArgumentException("Лимит CPU должен быть от 1 до 100%.");
    }
}
