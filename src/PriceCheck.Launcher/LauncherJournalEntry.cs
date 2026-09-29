namespace PriceCheck.Launcher;

public sealed record LauncherJournalEntry(DateTimeOffset Time, Guid? ProfileId, string Profile, string Message)
{
    public string Display => $"{Time.ToLocalTime():HH:mm:ss} [{Profile}] {Message}";
    public override string ToString() => Display;
}
