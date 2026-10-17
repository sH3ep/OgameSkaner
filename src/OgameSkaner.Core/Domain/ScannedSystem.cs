namespace OgameSkaner.Core.Domain;

/// <summary>A solar system that was scanned, also when it had no planets.</summary>
public readonly record struct ScannedSystem(SolarSystemAddress Address, DateTimeOffset ScannedAt);
