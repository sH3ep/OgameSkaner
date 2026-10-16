using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Services.Building;

/// <param name="Planets">The planets to build on; null means all of them.</param>
public sealed record BuildRequest(BuildStrategy Strategy, IReadOnlyCollection<PlanetId>? Planets = null);

public sealed record BuildProgress(string Message, int TotalQueued);

public enum BuildOutcome
{
    Completed,
    Cancelled,
    Failed
}

public sealed record PlanetBuildSummary(OwnPlanet Planet, int Queued, BuildStopReason Reason);

public sealed record BuildRunResult(BuildOutcome Outcome, int TotalQueued, IReadOnlyList<PlanetBuildSummary> Planets, string? Error = null);

public static class BuildStopReasons
{
    public static string Describe(BuildStopReason reason) => reason switch
    {
        BuildStopReason.TargetsReached => "all targets reached",
        BuildStopReason.NotEnoughResources => "not enough resources",
        BuildStopReason.QueueFull => "queue full",
        BuildStopReason.Rejected => "rejected by the server",
        BuildStopReason.NoFreeFields => "no free planet fields",
        BuildStopReason.VacationMode => "vacation mode",
        _ => "not finished"
    };
}
