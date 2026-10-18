using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Services.Defense;

namespace OgameSkaner.App.ViewModels;

/// <summary>One incoming hostile fleet in the Defense page's table.</summary>
public sealed class ThreatItemViewModel(Threat threat, DateTimeOffset now)
{
    public Threat Threat { get; } = threat;

    private FleetMovement Movement => Threat.Movement;

    public bool IsAttack => Movement.IsAttack;

    public string Mission => Movement.Mission.ToString();

    public string From => Movement.SenderName is { } sender ? $"{sender} [{Movement.Origin}]" : $"{Movement.OriginName} [{Movement.Origin}]";

    public string Target => $"{Movement.TargetName} [{Movement.Target}]";

    public long? Ships => Movement.ShipCount > 0 ? Movement.ShipCount : null;

    public string Arrives => Movement.ArrivesAt.ToLocalTime().ToString("T");

    public DateTimeOffset ArrivesSortKey => Movement.ArrivesAt;

    public string TimeLeft => Movement.ArrivesAt > now ? Format(Movement.ArrivesAt - now) : "";

    public string EscapeAt => Threat.EscapeAt?.ToLocalTime().ToString("T") ?? "";

    public string State => Threat.State;

    private static string Format(TimeSpan span) => span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
}

/// <summary>An alert channel the player can switch on or off.</summary>
public sealed partial class AlertChannelItemViewModel(IAlertChannel channel, bool enabled, Action changed) : ObservableObject
{
    public string Id => channel.Id;

    public string DisplayName => channel.DisplayName;

    [ObservableProperty]
    private bool _isEnabled = enabled;

    partial void OnIsEnabledChanged(bool value) => changed();
}
