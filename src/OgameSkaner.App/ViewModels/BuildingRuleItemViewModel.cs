using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.App.ViewModels;

/// <summary>One editable row of the build strategy.</summary>
public sealed partial class BuildingRuleItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private int _referenceLevel;

    public BuildingRuleItemViewModel(BuildingRule rule, int referenceLevel, Action changed)
    {
        var definition = BuildingCatalog.Get(rule.Building);
        Building = rule.Building;
        DisplayName = definition.DisplayName;
        Category = definition.Category;
        _enabled = rule.Enabled;
        _mode = rule.Mode;
        _value = rule.Value;
        _referenceLevel = referenceLevel;
        _changed = changed;
    }

    public static IReadOnlyList<RuleMode> Modes { get; } = Enum.GetValues<RuleMode>();

    public BuildingType Building { get; }

    public string DisplayName { get; }

    public BuildingCategory Category { get; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private RuleMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private int _value;

    public int Target => ToRule().TargetFor(_referenceLevel);

    public BuildingRule ToRule() => new(Building, Enabled, Mode, Value);

    public void UpdateReferenceLevel(int referenceLevel)
    {
        _referenceLevel = referenceLevel;
        OnPropertyChanged(nameof(Target));
    }

    partial void OnEnabledChanged(bool value) => _changed();

    partial void OnModeChanged(RuleMode value) => _changed();

    partial void OnValueChanged(int value) => _changed();
}
