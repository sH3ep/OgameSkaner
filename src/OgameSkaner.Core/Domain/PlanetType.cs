namespace OgameSkaner.Core.Domain;

/// <summary>Planet type ids used by the OgameX API.</summary>
public enum PlanetType
{
    Planet = 1,
    Moon = 2,

    /// <summary>A debris field (<c>window.planetType_DEBRIS_FIELD_Value</c>), the target of a Recycle mission.</summary>
    DebrisField = 3
}
