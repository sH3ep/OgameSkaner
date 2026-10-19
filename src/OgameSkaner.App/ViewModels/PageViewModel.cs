using CommunityToolkit.Mvvm.ComponentModel;

namespace OgameSkaner.App.ViewModels;

/// <summary>Base class for pages shown in the main window's content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Called every time the user navigates to the page.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}

/// <summary>Sent when the user saves new session cookies, so the session indicator re-checks them.</summary>
public sealed record SessionSavedMessage(string Universe);

public enum OperationStatus
{
    None,
    Pending,
    Succeeded,
    Failed
}

/// <summary>The attack protection's state, for the sidebar.</summary>
public enum ProtectionStatus
{
    Off,
    Watching,

    /// <summary>Hostile fleets are on their way.</summary>
    Threatened
}

public enum SessionStatus
{
    Unknown,
    Checking,
    Valid,
    Invalid
}
