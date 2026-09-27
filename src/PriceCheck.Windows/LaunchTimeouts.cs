namespace PriceCheck.Windows;

// Shared by both desktop products. Native equivalents live in launch_timeouts.h.
public static class LaunchTimeouts
{
    public const int GameStartupSeconds = 300;
    public const int AgentReadyMilliseconds = 60000;
    public const int HeartbeatMilliseconds = 15000;
    public const int WorldReadyMilliseconds = 120000;
    public const int LoginSeconds = 180;
    public const int NetworkSeconds = 30;
    public const int DnsSeconds = 45;
    public const int StopSeconds = 30;
    public const int DrainSeconds = 90;
    public const int DisconnectedSeconds = 60;
    public const int UnresponsiveSeconds = 180;
    public const int MissingWorldSeconds = 300;
    public const int DriverLoadSeconds = 300;
    public const int DriverReadySeconds = 15;
}
