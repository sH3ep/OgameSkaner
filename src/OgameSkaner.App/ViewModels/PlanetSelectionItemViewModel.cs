using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.App.ViewModels;

/// <summary>A planet with a checkbox saying whether automation runs on it.</summary>
public sealed partial class PlanetSelectionItemViewModel(OwnPlanet planet, bool isSelected, Action changed, bool isMoon = false) : ObservableObject
{
    public OwnPlanet Planet { get; } = planet;

    public bool IsMoon { get; } = isMoon;

    public string Name => Planet.Name;

    public string Coordinates => IsMoon ? $"[{Planet.Coordinates}] moon" : $"[{Planet.Coordinates}]";

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}
