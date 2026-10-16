using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Schemas;

/// <summary>One step of a build order: reach <paramref name="Level"/> of <paramref name="Building"/>.</summary>
public sealed record SchemaStep(BuildingType Building, int Level)
{
    public override string ToString() => $"{BuildingCatalog.Get(Building).DisplayName} {Level}";
}

/// <summary>Automatic additions to a schema, decided while it runs.</summary>
public sealed class SchemaRules
{
    /// <summary>The solar plant is raised up to this level whenever energy would run out.</summary>
    public int SolarMaxLevel { get; set; } = 20;

    /// <summary>When the solar plant is at its maximum (or locked) and energy is short, order solar satellites.</summary>
    public bool UseSolarSatellites { get; set; } = true;

    /// <summary>Queue a storage when its resource reaches this share of capacity; 0 turns it off.</summary>
    public int StorageFillPercent { get; set; } = 70;

    /// <summary>Highest Robotics Factory level the payback rule may build; 0 never.</summary>
    public int RoboticsMaxLevel { get; set; } = 10;

    /// <summary>Highest Nanite Factory level the payback rule may build; 0 never.</summary>
    public int NaniteMaxLevel { get; set; }

    /// <summary>
    /// How many items may be in the game's build queue (running one included). The game drops an item whose
    /// resources are missing when its turn comes, so the virtual queue only feeds a few at a time.
    /// </summary>
    public int MaxServerQueueDepth { get; set; } = 3;

    public SchemaRules Clone() => (SchemaRules)MemberwiseClone();
}

/// <summary>A named, ordered build plan that can be assigned to planets.</summary>
public sealed class BuildSchema
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New schema";

    public List<SchemaStep> Steps { get; set; } = [];

    public SchemaRules Rules { get; set; } = new();

    /// <summary>Appends the next level of <paramref name="building"/> (highest level already in the steps + 1).</summary>
    public SchemaStep AppendNextLevel(BuildingType building) => Steps[Insert(building, Steps.Count)];

    /// <summary>
    /// Inserts a step of <paramref name="building"/> at <paramref name="index"/> and renumbers, so it gets the level
    /// that fits its position. Returns the index of the new step.
    /// </summary>
    public int Insert(BuildingType building, int index)
    {
        index = Math.Clamp(index, 0, Steps.Count);
        Steps.Insert(index, new SchemaStep(building, 0));
        Renumber();
        return index;
    }

    public void RemoveAt(int index)
    {
        Steps.RemoveAt(index);
        Renumber();
    }

    /// <summary>Swaps the step at <paramref name="index"/> with its neighbour; false when it can't move that way.</summary>
    public bool Move(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= Steps.Count || target < 0 || target >= Steps.Count)
            return false;

        (Steps[index], Steps[target]) = (Steps[target], Steps[index]);
        Renumber();
        return true;
    }

    /// <summary>
    /// Numbers each building's steps in list order, counting up from the lowest level of that building in the
    /// schema (1 for a building's first step). A schema that starts mid-game (e.g. at Metal Mine 13) keeps its start.
    /// </summary>
    public void Renumber()
    {
        var next = Steps.Where(s => s.Level > 0)
            .GroupBy(s => s.Building)
            .ToDictionary(g => g.Key, g => g.Min(s => s.Level));

        for (var i = 0; i < Steps.Count; i++)
        {
            var building = Steps[i].Building;
            var level = next.GetValueOrDefault(building, 1);
            Steps[i] = Steps[i] with { Level = level };
            next[building] = level + 1;
        }
    }

    public BuildSchema Duplicate(string name) => new()
    {
        Name = name,
        Steps = [.. Steps],
        Rules = Rules.Clone()
    };

    public override string ToString() => Name;
}

/// <summary>What the schema planner wants to happen next on a planet.</summary>
public abstract record SchemaAction(string Reason)
{
    /// <summary>Queue the next level of a building.</summary>
    public sealed record Upgrade(BuildingType Building, int Level, ResourceSet Cost, string Reason) : SchemaAction(Reason);

    /// <summary>Order solar satellites in the shipyard.</summary>
    public sealed record Satellites(int Count, ResourceSet Cost, string Reason) : SchemaAction(Reason);

    /// <summary>Nothing to do right now (resources, queue depth, requirements…); check again later.</summary>
    public sealed record Wait(string Reason) : SchemaAction(Reason);

    /// <summary>Every step of the schema is built or queued.</summary>
    public sealed record Done() : SchemaAction("all steps are built or queued");
}
