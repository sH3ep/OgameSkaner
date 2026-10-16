using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// One editable row of the build strategy. Relative rules are edited through the offset, absolute rules
/// through the target level; switching the mode keeps the resulting target.
/// </summary>
public sealed partial class BuildingRuleItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private int _referenceLevel;
    private int _targetBeforeModeChange;

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
    [NotifyPropertyChangedFor(nameof(Target), nameof(IsRelative), nameof(IsAbsolute))]
    private RuleMode _mode;

    /// <summary>The offset for relative rules, the target level for absolute ones (as stored).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Target))]
    private int _value;

    /// <summary>Level on the planet selected in the planet list, e.g. "24" or "24 → 27" with queued levels.</summary>
    [ObservableProperty]
    private string? _actual;

    public bool IsRelative => Mode == RuleMode.Relative;

    public bool IsAbsolute => Mode == RuleMode.Absolute;

    /// <summary>The level to reach; editable for absolute rules, calculated from the reference level for relative ones.</summary>
    public int Target
    {
        get => ToRule().TargetFor(_referenceLevel);
        set
        {
            if (IsAbsolute)
                Value = Math.Max(0, value);
        }
    }

    public BuildingRule ToRule() => new(Building, Enabled, Mode, Value);

    public void UpdateReferenceLevel(int referenceLevel)
    {
        _referenceLevel = referenceLevel;
        OnPropertyChanged(nameof(Target));
    }

    partial void OnEnabledChanged(bool value) => _changed();

    partial void OnModeChanging(RuleMode value) => _targetBeforeModeChange = Target;

    partial void OnModeChanged(RuleMode value)
    {
        // Keep the level the rule aims at: Relative +2 at reference 10 becomes Absolute 12 and back.
        Value = value == RuleMode.Absolute ? _targetBeforeModeChange : _targetBeforeModeChange - _referenceLevel;
        _changed();
    }

    partial void OnValueChanged(int value) => _changed();
}
