namespace OgameSkaner.Core.Services.Scheduling;

/// <summary>
/// The wait between two cycles of a continuous runner, which the player can cut short ("Check now"). A call to
/// <see cref="Now"/> while a cycle runs ends the next wait at once, so it isn't lost.
/// </summary>
public sealed class WakeUp
{
    private TaskCompletionSource _signal = NewSignal();

    /// <summary>Ends the current (or the next) wait.</summary>
    public void Now() => Volatile.Read(ref _signal).TrySetResult();

    /// <summary>Forgets a <see cref="Now"/> from before, e.g. when a run starts.</summary>
    public void Reset() => Interlocked.Exchange(ref _signal, NewSignal());

    /// <summary>Waits <paramref name="delay"/> or until <see cref="Now"/>; true when woken early.</summary>
    public async Task<bool> DelayAsync(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var signal = Volatile.Read(ref _signal);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timer = Task.Delay(delay, timeProvider, stop.Token);
        var first = await Task.WhenAny(timer, signal.Task).ConfigureAwait(false);
        stop.Cancel(); // the timer isn't needed any more
        cancellationToken.ThrowIfCancellationRequested();
        if (first == timer)
            return false;

        Interlocked.CompareExchange(ref _signal, NewSignal(), signal);
        return true;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
