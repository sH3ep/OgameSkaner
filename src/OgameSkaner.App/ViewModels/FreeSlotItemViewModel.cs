using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;

namespace OgameSkaner.App.ViewModels;

/// <summary>One row of the colonization list: a free slot in a scanned system, with a colonize action.</summary>
public sealed partial class FreeSlotItemViewModel(FreeSlot slot, ColonizationService colonization, IOgamexClient client) : ObservableObject
{
    public FreeSlot Slot { get; } = slot;

    public string Coordinates => Slot.Coordinates.ToString();

    public string PlanetsInSystem => $"{Slot.PlanetsInSystem} planets";

    public string ScannedAt => $"Scanned {Slot.ScannedAt.ToLocalTime():g}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ColonizeCommand))]
    private OperationStatus _colonizeStatus;

    [ObservableProperty]
    private string? _colonizeMessage;

    /// <summary>Once a Colony Ship is on its way (or while one is being sent) the slot can't be colonized again.</summary>
    private bool CanColonize() => ColonizeStatus is not (OperationStatus.Pending or OperationStatus.Succeeded);

    [RelayCommand(CanExecute = nameof(CanColonize))]
    private async Task ColonizeAsync()
    {
        ColonizeStatus = OperationStatus.Pending;
        ColonizeMessage = "Sending…";
        try
        {
            var result = await colonization.ColonizeAsync(client, Slot.Coordinates);
            (ColonizeStatus, ColonizeMessage) = (result.Sent ? OperationStatus.Succeeded : OperationStatus.Failed, result.Message);
        }
        catch (OgamexException ex)
        {
            (ColonizeStatus, ColonizeMessage) = (OperationStatus.Failed, ex.Message);
        }
    }
}
