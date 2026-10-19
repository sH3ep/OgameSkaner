namespace OgameSkaner.Core.Domain.Alerts;

public enum AlertSeverity
{
    /// <summary>Something was done, e.g. a fleet escaped.</summary>
    Info,

    /// <summary>Worth a look, e.g. a hostile spy probe.</summary>
    Warning,

    /// <summary>Needs the player now, e.g. an attack or a failed escape.</summary>
    Critical
}

/// <summary>A message for the player, sent through every enabled <see cref="IAlertChannel"/>.</summary>
public sealed record Alert(string Universe, string Title, string Message, AlertSeverity Severity);

/// <summary>
/// One way to reach the player: a Windows notification, a sound, later e-mail or SMS. Register an implementation in
/// DI and it shows up on the Defense page, where the player switches it on or off.
/// </summary>
public interface IAlertChannel
{
    /// <summary>Stable key saved in <see cref="AlertSettings.EnabledChannels"/>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Delivers the alert; may throw, the dispatcher logs it and goes on with the other channels.</summary>
    Task SendAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>How alerts reach the player, shared by all universes (<c>alert-settings.json</c>).</summary>
public sealed record AlertSettings
{
    /// <summary>The <see cref="IAlertChannel.Id"/>s alerts go to.</summary>
    public IReadOnlyList<string> EnabledChannels { get; init; } = ["windows", "sound"];

    /// <summary>A .wav or .mp3 file the sound channel plays; null = the app's alarm signal.</summary>
    public string? SoundFile { get; init; }

    /// <summary>How often the sound plays per alert.</summary>
    public int SoundRepeat { get; init; } = 3;

    public bool IsEnabled(string channelId) => EnabledChannels.Contains(channelId, StringComparer.OrdinalIgnoreCase);

    public static AlertSettings Default { get; } = new();
}
