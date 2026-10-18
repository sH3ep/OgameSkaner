using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.App.ViewModels;

/// <summary>A ship type with a checkbox: the cargo ships the raider may use (the order is the priority) or the ships that fly on expeditions.</summary>
public sealed partial class CargoShipOptionViewModel(UnitInfo unit, bool isSelected, Action changed) : ObservableObject
{
    public string Id => unit.Id;

    public string Name => unit.Name;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}
