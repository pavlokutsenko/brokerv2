using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PriceCheck.Collector.Services;

internal static class GatewayIdentityService
{
    public static IPAddress? FindIpv4Gateway() => NetworkInterface.GetAllNetworkInterfaces()
        .SelectMany(adapter => adapter.GetIPProperties().GatewayAddresses)
        .Select(address => address.Address)
        .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
}
