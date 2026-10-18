using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.App.ViewModels;

/// <summary>A planet or moon on the Expeditions page: whether it sends expeditions and how many at a time.</summary>
public sealed partial class ExpeditionOriginItemViewModel(OwnPlanet place, bool isMoon, bool isUsed, int count, Action changed) : ObservableObject
{
    public OwnPlanet Place { get; } = place;

    public string Id => Place.Id.Value;

    public string Name => Place.Name;

    public string Coordinates => $"[{Place.Coordinates}]";

    public string Type => isMoon ? "Moon" : "Planet";

    [ObservableProperty]
    private bool _isUsed = isUsed;

    [ObservableProperty]
    private int _count = count;

    /// <summary>The expeditions in the air from here at the last check; null before one.</summary>
    [ObservableProperty]
    private int? _flying;

    partial void OnIsUsedChanged(bool value) => changed();

    partial void OnCountChanged(int value) => changed();
}
