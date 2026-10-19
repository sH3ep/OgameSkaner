using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace OgameSkaner.App.Infrastructure.Alerts;

/// <summary>
/// Plays the alarm sounds of <see cref="SoundAlertChannel"/>: a sound file (.wav or .mp3), else the app's alarm
/// (<see cref="DefaultSoundFile"/>), else the Windows alarm sound. <see cref="StopCommand"/> silences what plays now;
/// the next alert plays again.
/// </summary>
public sealed partial class AlarmPlayer(ILogger<AlarmPlayer> logger) : ObservableObject
{
    private static readonly TimeSpan SystemSoundGap = TimeSpan.FromSeconds(1.2);

    private readonly object _sync = new();
    private CancellationTokenSource _stop = new();
    private int _playing;

    /// <summary>The alarm shipped with the app, used when no sound file is chosen.</summary>
    public static string DefaultSoundFile { get; } = Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds", "alarmSignal.mp3");

    /// <summary>Whether a sound plays now (the sidebar's "Stop alarm" button shows then).</summary>
    public bool IsPlaying => Volatile.Read(ref _playing) > 0;

    /// <summary>Plays <paramref name="file"/> (null = the app's alarm) <paramref name="repeat"/> times, for at most <paramref name="limit"/>.</summary>
    public async Task PlayAsync(string? file, int repeat, TimeSpan? limit, CancellationToken cancellationToken = default)
    {
        CancellationToken stop;
        lock (_sync)
            stop = _stop.Token;
        using var playing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop);
        if (limit is { } max)
            playing.CancelAfter(max);

        SetPlaying(+1);
        try
        {
            await PlayCoreAsync(string.IsNullOrWhiteSpace(file) ? DefaultSoundFile : file, repeat, playing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stopped by the player or the time limit.
        }
        finally
        {
            SetPlaying(-1);
        }
    }

    /// <summary>Silences the sounds playing now.</summary>
    [RelayCommand]
    private void Stop()
    {
        CancellationTokenSource stopped;
        lock (_sync)
        {
            stopped = _stop;
            _stop = new CancellationTokenSource();
        }
        stopped.Cancel();
    }

    private async Task PlayCoreAsync(string file, int repeat, CancellationToken cancellationToken)
    {
        if (File.Exists(file) && Application.Current?.Dispatcher is { } dispatcher)
        {
            try
            {
                // MediaPlayer (unlike SoundPlayer) plays .mp3 too, and needs a dispatcher thread.
                await dispatcher.InvokeAsync(() => PlayFileAsync(file, repeat, cancellationToken)).Task.Unwrap().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Playing {File} failed; using the Windows alarm sound", file);
            }
        }

        await Task.Run(() => PlaySystemSound(repeat, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static async Task PlayFileAsync(string file, int repeat, CancellationToken cancellationToken)
    {
        var player = new MediaPlayer();
        // One per play; the handlers complete the current one (a failure while opening completes the first).
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaEnded += (_, _) => ended.TrySetResult();
        player.MediaFailed += (_, e) => ended.TrySetException(e.ErrorException);
        try
        {
            player.Open(new Uri(file, UriKind.Absolute));
            await using var stop = cancellationToken.Register(() => ended.TrySetCanceled(cancellationToken));
            for (var i = 0; i < repeat; i++)
            {
                if (i > 0)
                    ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.ThrowIfCancellationRequested();
                player.Position = TimeSpan.Zero;
                player.Play();
                await ended.Task;
            }
        }
        finally
        {
            player.Close();
        }
    }

    private static void PlaySystemSound(int repeat, CancellationToken cancellationToken)
    {
        for (var i = 0; i < repeat && !cancellationToken.IsCancellationRequested; i++)
        {
            SystemSounds.Hand.Play();
            cancellationToken.WaitHandle.WaitOne(SystemSoundGap);
        }
    }

    private void SetPlaying(int change)
    {
        Interlocked.Add(ref _playing, change);
        OnPropertyChanged(nameof(IsPlaying));
    }
}
