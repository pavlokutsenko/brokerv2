namespace PriceCheck.Collector.Runtime.Radar;

public abstract record WorldPacket;
public sealed record CharacterPacket(int ObjectId, string Name, string Title, int KioskType, int X, int Y, int Z) : WorldPacket;
public sealed record MovePacket(int ObjectId, int X, int Y, int Z) : WorldPacket;
public sealed record DeletePacket(int ObjectId) : WorldPacket;

