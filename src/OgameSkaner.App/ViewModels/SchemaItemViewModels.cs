using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.App.ViewModels;

/// <summary>A schema in the editor; edits go straight to the model and are reported through <c>changed</c>.</summary>
public sealed partial class SchemaItemViewModel : ObservableObject
{
    private readonly Action _changed;

    public SchemaItemViewModel(BuildSchema schema, Action changed)
    {
        Model = schema;
        _changed = changed;
        RebuildSteps();
    }

    public BuildSchema Model { get; }

    public ObservableCollection<SchemaStepItemViewModel> Steps { get; } = [];

    public string Name
    {
        get => Model.Name;
        set => Set(Model.Name, value, v => Model.Name = string.IsNullOrWhiteSpace(v) ? "Unnamed" : v.Trim());
    }

    public int SolarMaxLevel
    {
        get => Model.Rules.SolarMaxLevel;
        set => Set(Model.Rules.SolarMaxLevel, value, v => Model.Rules.SolarMaxLevel = Math.Max(0, v));
    }

    public bool UseSolarSatellites
    {
        get => Model.Rules.UseSolarSatellites;
        set => Set(Model.Rules.UseSolarSatellites, value, v => Model.Rules.UseSolarSatellites = v);
    }

    public int StorageFillPercent
    {
        get => Model.Rules.StorageFillPercent;
        set => Set(Model.Rules.StorageFillPercent, value, v => Model.Rules.StorageFillPercent = Math.Clamp(v, 0, 100));
    }

    public int RoboticsMaxLevel
    {
        get => Model.Rules.RoboticsMaxLevel;
        set => Set(Model.Rules.RoboticsMaxLevel, value, v => Model.Rules.RoboticsMaxLevel = Math.Max(0, v));
    }

    public int NaniteMaxLevel
    {
        get => Model.Rules.NaniteMaxLevel;
        set => Set(Model.Rules.NaniteMaxLevel, value, v => Model.Rules.NaniteMaxLevel = Math.Max(0, v));
    }

    public int MaxServerQueueDepth
    {
        get => Model.Rules.MaxServerQueueDepth;
        set => Set(Model.Rules.MaxServerQueueDepth, value, v => Model.Rules.MaxServerQueueDepth = Math.Clamp(v, 1, 15));
    }

    public string Summary => $"{Model.Steps.Count} steps";

    public void AddStep(BuildingType building)
    {
        Model.AppendNextLevel(building);
        StepsChanged();
    }

    public void RemoveStep(int index)
    {
        Model.Steps.RemoveAt(index);
        StepsChanged();
    }

    public void MoveStep(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= Model.Steps.Count)
            return;

        (Model.Steps[index], Model.Steps[target]) = (Model.Steps[target], Model.Steps[index]);
        StepsChanged();
    }

    /// <summary>Marks the steps for a planet: ✓ built, ⏳ queued, ▶ next; null clears the marks.</summary>
    public void ShowProgress(PlanetBuildState? state)
    {
        var nextFound = false;
        foreach (var step in Steps)
        {
            if (state is null)
                step.Mark = null;
            else if (state.LevelOf(step.Step.Building) >= step.Step.Level)
                step.Mark = "✓";
            else if (state.HasReached(step.Step.Building, step.Step.Level))
                step.Mark = "⏳";
            else
            {
                step.Mark = nextFound ? null : "▶";
                nextFound = true;
            }
        }
    }

    public override string ToString() => Name;

    private void StepsChanged()
    {
        RebuildSteps();
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void RebuildSteps()
    {
        Steps.Clear();
        for (var i = 0; i < Model.Steps.Count; i++)
            Steps.Add(new SchemaStepItemViewModel(i, Model.Steps[i]));
    }

    private void Set<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        apply(value);
        OnPropertyChanged(property);
        _changed();
    }
}

/// <summary>One row of the step list.</summary>
public sealed partial class SchemaStepItemViewModel(int index, SchemaStep step) : ObservableObject
{
    public int Index { get; } = index;

    public SchemaStep Step { get; } = step;

    public string Text => $"{Index + 1}. {Step}";

    /// <summary>Progress on the viewed planet: ✓ built, ⏳ queued, ▶ next.</summary>
    [ObservableProperty]
    private string? _mark;
}

/// <summary>A planet in the runner list: whether it runs, which schema it follows and what it is doing.</summary>
public sealed partial class SchemaPlanetItemViewModel(OwnPlanet planet, bool enabled, SchemaItemViewModel? schema, Action changed) : ObservableObject
{
    public OwnPlanet Planet { get; } = planet;

    public string Name => Planet.Name;

    public string Coordinates => $"[{Planet.Coordinates}]";

    [ObservableProperty]
    private bool _isEnabled = enabled;

    [ObservableProperty]
    private SchemaItemViewModel? _schema = schema;

    [ObservableProperty]
    private string? _status;

    partial void OnIsEnabledChanged(bool value) => changed();

    partial void OnSchemaChanged(SchemaItemViewModel? value) => changed();
}
