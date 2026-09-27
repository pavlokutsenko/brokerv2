namespace PriceCheck.Collector.Services;

public sealed class LaunchProtectionException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
