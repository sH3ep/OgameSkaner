namespace OgameSkaner.Core.Ogamex;

// Request/response bodies of the OgameX JSON endpoints (serialized with camelCase names).

internal sealed record SpyRequest(int X, int Y, int Z, int PlanetType);

internal sealed record SpyResponse(bool IsSuccess);

internal sealed record SendFleetRequest(
    IReadOnlyList<ShipRequest> Ships,
    int TargetX,
    int TargetY,
    int TargetZ,
    int TargetPlanetType,
    int SpeedPercentage,
    long Metal,
    long Crystal,
    long Deuterium,
    int MissionType);

internal sealed record ShipRequest(string ShipType, long Quantity);
