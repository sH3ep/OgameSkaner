using System.IO;
using System.Media;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.Infrastructure.Alerts;

/// <summary>
/// Plays a sound for warnings and critical alerts (not for info like "fleet escaped"): the chosen .wav file, or the
/// Windows alarm sound. Critical alerts repeat it <see cref="AlertSettings.SoundRepeat"/> times.
/// </summary>
public sealed class SoundAlertChannel(IDefenseSettingsStore store) : IAlertChannel
{
    private static readonly TimeSpan SystemSoundGap = TimeSpan.FromSeconds(1.2);

    public string Id => "sound";

    public string DisplayName => "Sound";

    public Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        if (alert.Severity == AlertSeverity.Info)
            return Task.CompletedTask;

        var settings = store.LoadAlerts();
        var repeat = alert.Severity == AlertSeverity.Critical ? Math.Clamp(settings.SoundRepeat, 1, 20) : 1;
        return Task.Run(() => Play(settings.SoundFile, repeat, cancellationToken), cancellationToken);
    }

    private static void Play(string? file, int repeat, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            using var player = new SoundPlayer(file);
            player.Load();
            for (var i = 0; i < repeat && !cancellationToken.IsCancellationRequested; i++)
                player.PlaySync();
            return;
        }

        for (var i = 0; i < repeat && !cancellationToken.IsCancellationRequested; i++)
        {
            SystemSounds.Hand.Play();
            Thread.Sleep(SystemSoundGap);
        }
    }
}
