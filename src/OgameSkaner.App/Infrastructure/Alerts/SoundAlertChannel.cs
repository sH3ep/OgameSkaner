using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.Infrastructure.Alerts;

/// <summary>
/// Plays a sound (through <see cref="AlarmPlayer"/>) for warnings and critical alerts, not for info like "fleet
/// escaped": critical alerts (attacks) repeat it <see cref="AlertSettings.SoundRepeat"/> times, warnings (spy probes)
/// play it for <see cref="WarningLimit"/> at most.
/// </summary>
public sealed class SoundAlertChannel(IDefenseSettingsStore store, AlarmPlayer player) : IAlertChannel
{
    public static readonly TimeSpan WarningLimit = TimeSpan.FromSeconds(3);

    public string Id => "sound";

    public string DisplayName => "Sound";

    public Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (alert.Severity == AlertSeverity.Info)
            return Task.CompletedTask;

        var settings = store.LoadAlerts();
        return alert.Severity == AlertSeverity.Critical
            ? player.PlayAsync(settings.SoundFile, Math.Clamp(settings.SoundRepeat, 1, 20), null, cancellationToken)
            : player.PlayAsync(settings.SoundFile, 1, WarningLimit, cancellationToken);
    }
}
