using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.App.ViewModels;

/// <summary>A planet with a checkbox saying whether automation runs on it.</summary>
public sealed partial class PlanetSelectionItemViewModel(OwnPlanet planet, bool isSelected, Action changed) : ObservableObject
{
    public OwnPlanet Planet { get; } = planet;

    public string Name => Planet.Name;

    public string Coordinates => $"[{Planet.Coordinates}]";

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}
