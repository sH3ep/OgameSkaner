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

/// <summary>Answer of <c>/fleet/submitfleet</c>: <c>{"IsSuccess":true,"Message":null,"ValidationErrors":null}</c>.</summary>
internal sealed record SubmitFleetResponse(bool IsSuccess, string? Message);

/// <summary>Body of <c>/fleet/fleetpage3data</c> (the briefing before sending).</summary>
internal sealed record FleetBriefingRequest(
    IReadOnlyList<ShipRequest> Ships,
    int TargetX,
    int TargetY,
    int TargetZ,
    int TargetPlanetType,
    int SpeedPercentage);

/// <summary>Answer of <c>/fleet/fleetpage3data</c>; see docs/ogamex-raid-api.md.</summary>
internal sealed record FleetBriefingResponse(
    bool IsSuccess,
    string? Message,
    int[]? AvailableMissions,
    long FleetOneWayTimeOnSeconds,
    long DeuteriumConsumption,
    long Distance,
    string? PlayerId,
    string? PlayerName,
    int? PlayerRanking,
    long PlayerPoints,
    long PlayerFleetPoints,
    bool PlayerIsVacation,
    bool PlayerIsBanned,
    bool PlayerIsNoob,
    bool PlayerIsStrong,
    bool PlayerIsInactive7,
    bool PlayerIsInactive28,
    bool PlayerIsUnderProtection,
    bool ShowWarning,
    string? WarningMessage);

/// <summary>Body of <c>/building/increasebuilding</c>; the game sends these exact (PascalCase) names.</summary>
internal sealed record IncreaseBuildingRequest(
    [property: JsonPropertyName("BuildingType")] string BuildingType,
    [property: JsonPropertyName("__PlanetId")] string PlanetId);

/// <summary>Body of <c>/hangar/createship</c>; the game sends these exact (PascalCase) names.</summary>
internal sealed record CreateShipRequest(
    [property: JsonPropertyName("ShipType")] string ShipType,
    [property: JsonPropertyName("Quantity")] int Quantity,
    [property: JsonPropertyName("__PlanetId")] string PlanetId);

internal sealed record CreateDefenseRequest(
    [property: JsonPropertyName("DefenseType")] string DefenseType,
    [property: JsonPropertyName("Quantity")] int Quantity,
    [property: JsonPropertyName("__PlanetId")] string PlanetId);

/// <summary>Answer of the building endpoints: <c>{"IsSuccess":false,"Message":"Construction queue is full.","ValidationErrors":null}</c>.</summary>
internal sealed record BuildingResponse(bool IsSuccess, string? Message);
