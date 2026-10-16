using System.Text.Json.Serialization;

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

/// <summary>Body of <c>/building/increasebuilding</c>; the game sends these exact (PascalCase) names.</summary>
internal sealed record IncreaseBuildingRequest(
    [property: JsonPropertyName("BuildingType")] string BuildingType,
    [property: JsonPropertyName("__PlanetId")] string PlanetId);

/// <summary>Body of <c>/hangar/createship</c>; the game sends these exact (PascalCase) names.</summary>
internal sealed record CreateShipRequest(
    [property: JsonPropertyName("ShipType")] string ShipType,
    [property: JsonPropertyName("Quantity")] int Quantity,
    [property: JsonPropertyName("__PlanetId")] string PlanetId);

/// <summary>Answer of the building endpoints: <c>{"IsSuccess":false,"Message":"Construction queue is full.","ValidationErrors":null}</c>.</summary>
internal sealed record BuildingResponse(bool IsSuccess, string? Message);
