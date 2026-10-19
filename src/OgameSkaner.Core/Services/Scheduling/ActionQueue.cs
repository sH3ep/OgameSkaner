using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Scheduling;

/// <summary>Pacing of an <see cref="ActionQueue"/>.</summary>
/// <param name="MinGap">Shortest pause between the end of one group and the start of the next.</param>
/// <param name="MaxGap">Longest such pause; each pause is random in between.</param>
/// <param name="ReturnHomeAfter">
/// Idle time after which the game is switched back to <see cref="IActionQueue.HomePlanet"/>; null never switches back
/// (and doesn't read the home planet either).
/// </param>
public sealed record ActionQueueSettings(TimeSpan MinGap, TimeSpan MaxGap, TimeSpan? ReturnHomeAfter)
{
    /// <summary>No pauses, no switching back: groups run one after another as soon as they're queued.</summary>
    public static ActionQueueSettings Immediate { get; } = new(TimeSpan.Zero, TimeSpan.Zero, null);

    public static ActionQueueSettings From(OgamexOptions options) => new(
        options.ActionGapMin,
        options.ActionGapMax < options.ActionGapMin ? options.ActionGapMin : options.ActionGapMax,
        options.ReturnHomeAfter > TimeSpan.Zero ? options.ReturnHomeAfter : null);
}

/// <summary>
/// <see cref="IActionQueue"/> of one client. A single pump task runs the groups while there are any: player groups first,
/// then the modules in turn (one group of each owner before the next of the same one, first come first served within an
/// owner), so a module that queues many groups can't hold up the others. The pump ends once it switched back home.
/// Every finished group also goes to <paramref name="history"/> (when given) for later analysis.
/// </summary>
public sealed class ActionQueue(IOgamexClient client, ActionQueueSettings settings, TimeProvider timeProvider, ILogger logger, IActionHistoryStore? history = null)
    : IActionQueue
{
    private const int FinishedKept = 50;

    /// <summary>The queue whose group runs in this async flow: a group that queues more work runs it inline.</summary>
    private static readonly AsyncLocal<ActionQueue?> Inside = new();

    private readonly object _sync = new();
    private readonly List<Entry> _waiting = [];
    private readonly List<Entry> _running = [];
    private readonly LinkedList<ActionGroupInfo> _finished = new();
    private readonly SemaphoreSlim _arrived = new(0);
    private readonly List<string> _ownerTurns = [];

    private long _nextId;
    private Task? _pump;
    private bool _busy;
    private PlanetId? _home;
    private DateTimeOffset? _lastFinished;

    /// <summary>The owner of the last group that ran; the next group pauses only when it's another owner's.</summary>
    private string? _lastOwner;

    public event EventHandler? Changed;

    public PlanetId? HomePlanet
    {
        get
        {
            lock (_sync)
                return _home;
        }
    }

    public async Task<T> RunAsync<T>(
        string owner,
        string description,
        Func<CancellationToken, Task<T>> work,
        ActionPriority priority = ActionPriority.Automation,
        CancellationToken cancellationToken = default)
    {
        if (priority == ActionPriority.Urgent)
            return await RunUrgentAsync(owner, description, work, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (Inside.Value == this)
            return await work(cancellationToken).ConfigureAwait(false); // waiting for the queue here would never end

        T result = default!;
        var entry = new Entry(NewInfo(owner, description, priority), async token => result = await work(token).ConfigureAwait(false), cancellationToken);
        lock (_sync)
        {
            _waiting.Add(entry);
            _pump ??= Task.Run(PumpAsync);
        }
        _arrived.Release();
        OnChanged();

        // Taken off the queue when cancelled before it started; a running group gets the token itself.
        await using (cancellationToken.Register(() => CancelWaiting(entry)).ConfigureAwait(false))
            await entry.Completion.Task.ConfigureAwait(false);
        return result;
    }

    public async Task<T> RunUrgentAsync<T>(string owner, string description, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        var entry = new Entry(NewInfo(owner, description, ActionPriority.Urgent), _ => Task.CompletedTask, cancellationToken);
        bool captureHome;
        lock (_sync)
        {
            Start(entry);
            captureHome = !_busy;
            _busy = true;
        }
        OnChanged();

        Inside.Value = this;
        try
        {
            if (captureHome)
                await CaptureHomeAsync().ConfigureAwait(false);
            var result = await work(cancellationToken).ConfigureAwait(false);
            Finish(entry, ActionGroupState.Done, null);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Finish(entry, ActionGroupState.Cancelled, null);
            throw;
        }
        catch (Exception ex)
        {
            Finish(entry, ActionGroupState.Failed, ex.Message);
            throw;
        }
        finally
        {
            // The pump switches back home once nothing runs any more.
            lock (_sync)
                _pump ??= Task.Run(PumpAsync);
            _arrived.Release();
        }
    }

    public ActionQueueSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new ActionQueueSnapshot(
                _running.Select(e => e.Info).ToList(),
                Ordered().Select(e => e.Info).ToList(),
                _finished.ToList(),
                _home);
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            bool hasWork, firstOfBusy;
            lock (_sync)
            {
                hasWork = _running.Count == 0 && _waiting.Count > 0;
                firstOfBusy = hasWork && !_busy;
                if (hasWork)
                    _busy = true;
            }

            if (!hasWork)
            {
                if (await IdleAsync().ConfigureAwait(false))
                    return;
                continue;
            }

            if (firstOfBusy)
                await CaptureHomeAsync().ConfigureAwait(false);

            // The same owner's next group runs at once; another owner's after the pause, picked again afterwards (a
            // player group queued meanwhile goes first, a cancelled one is gone).
            Entry? next;
            var paused = false;
            while (true)
            {
                bool taken;
                lock (_sync)
                {
                    next = _running.Count == 0 ? Ordered().FirstOrDefault() : null;
                    taken = next is not null && (paused || next.Info.Owner == _lastOwner);
                    if (taken)
                    {
                        _waiting.Remove(next!);
                        Start(next!);
                        _ownerTurns.Remove(next!.Info.Owner);
                        _ownerTurns.Add(next.Info.Owner);
                    }
                }

                if (next is null || taken)
                    break;
                await PauseAsync().ConfigureAwait(false);
                paused = true;
            }

            if (next is null)
                continue;
            OnChanged();
            await RunEntryAsync(next).ConfigureAwait(false);
        }
    }

    private async Task RunEntryAsync(Entry entry)
    {
        Inside.Value = this;
        try
        {
            await entry.Work(entry.CancellationToken).ConfigureAwait(false);
            Finish(entry, ActionGroupState.Done, null);
            entry.Completion.TrySetResult();
        }
        catch (OperationCanceledException) when (entry.CancellationToken.IsCancellationRequested)
        {
            Finish(entry, ActionGroupState.Cancelled, null);
            entry.Completion.TrySetCanceled(entry.CancellationToken);
        }
        catch (Exception ex)
        {
            Finish(entry, ActionGroupState.Failed, ex.Message);
            entry.Completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// Waits for a new group while nothing runs; after <see cref="ActionQueueSettings.ReturnHomeAfter"/> without one it
    /// switches back home. Returns true when the pump has ended.
    /// </summary>
    private async Task<bool> IdleAsync()
    {
        var urgentRunning = false;
        lock (_sync)
            urgentRunning = _running.Count > 0;

        if (urgentRunning || settings.ReturnHomeAfter is null)
        {
            if (!urgentRunning && TryEnd())
                return true;
            await _arrived.WaitAsync().ConfigureAwait(false);
            return false;
        }

        if (await WaitForArrivalAsync(settings.ReturnHomeAfter.Value).ConfigureAwait(false))
            return false;

        await ReturnHomeAsync().ConfigureAwait(false);
        return TryEnd();
    }

    /// <summary>True when a group arrived (or an urgent one ended) within <paramref name="timeout"/>.</summary>
    private async Task<bool> WaitForArrivalAsync(TimeSpan timeout)
    {
        using var stop = new CancellationTokenSource();
        var arrival = _arrived.WaitAsync(stop.Token);
        var first = await Task.WhenAny(arrival, Task.Delay(timeout, timeProvider, stop.Token)).ConfigureAwait(false);
        stop.Cancel();
        try
        {
            // The arrival may have come at the same moment as the timeout; then it counts.
            await arrival.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (first != arrival)
        {
            return false;
        }
    }

    /// <summary>Ends the pump unless something was queued or started meanwhile.</summary>
    private bool TryEnd()
    {
        bool ended;
        lock (_sync)
        {
            ended = _waiting.Count == 0 && _running.Count == 0;
            if (ended)
            {
                _pump = null;
                _busy = false;
                _home = null;
            }
        }
        if (ended)
            OnChanged();
        return ended;
    }

    private async Task CaptureHomeAsync()
    {
        if (settings.ReturnHomeAfter is null)
            return;

        try
        {
            var snapshot = await client.GetOwnPlanetsAsync().ConfigureAwait(false);
            lock (_sync)
                _home = snapshot.CurrentPlanet;
            OnChanged();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading the selected planet of {Universe} failed; the game won't be switched back", client.Universe.Name);
        }
    }

    private async Task ReturnHomeAsync()
    {
        var home = HomePlanet;
        if (home is null || client.CurrentPlanet == home)
            return;

        try
        {
            await client.SelectPlanetAsync(home.Value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Switching {Universe} back to planet {Planet} failed", client.Universe.Name, home);
        }
    }

    /// <summary>The random pause between two owners' groups, counted from the end of the previous one.</summary>
    private async Task PauseAsync()
    {
        if (settings.MaxGap <= TimeSpan.Zero || _lastFinished is not { } last)
            return;

        var gap = settings.MinGap + (settings.MaxGap - settings.MinGap) * Random.Shared.NextDouble();
        var wait = last + gap - timeProvider.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, timeProvider).ConfigureAwait(false);
    }

    /// <summary>The waiting groups in the order they will run.</summary>
    private List<Entry> Ordered()
    {
        // The owner that ran longest ago (or never) goes first; within an owner, first come first served.
        int Turn(string owner)
        {
            var index = _ownerTurns.IndexOf(owner);
            return index < 0 ? -1 : index;
        }

        return _waiting
            .OrderByDescending(e => e.Info.Priority)
            .ThenBy(e => e.Info.Priority == ActionPriority.User ? 0 : Turn(e.Info.Owner))
            .ThenBy(e => e.Info.Id)
            .ToList();
    }

    private void CancelWaiting(Entry entry)
    {
        bool removed;
        lock (_sync)
            removed = _waiting.Remove(entry);
        if (!removed)
            return;

        entry.Completion.TrySetCanceled(entry.CancellationToken);
        Finish(entry, ActionGroupState.Cancelled, null, started: false);
    }

    private ActionGroupInfo NewInfo(string owner, string description, ActionPriority priority) =>
        new(Interlocked.Increment(ref _nextId), owner, description, priority, timeProvider.GetLocalNow()) { State = ActionGroupState.Waiting };

    /// <summary>Marks the entry as running; the caller holds the lock.</summary>
    private void Start(Entry entry)
    {
        entry.Info = entry.Info with { State = ActionGroupState.Running, StartedAt = timeProvider.GetLocalNow() };
        _running.Add(entry);
    }

    private void Finish(Entry entry, ActionGroupState state, string? error, bool started = true)
    {
        lock (_sync)
        {
            _running.Remove(entry);
            entry.Info = entry.Info with { State = state, FinishedAt = timeProvider.GetLocalNow(), Error = error };
            _finished.AddFirst(entry.Info);
            while (_finished.Count > FinishedKept)
                _finished.RemoveLast();
            if (started && entry.Info.Priority != ActionPriority.Urgent)
            {
                _lastFinished = timeProvider.GetUtcNow();
                _lastOwner = entry.Info.Owner;
            }
        }

        if (state == ActionGroupState.Failed)
            logger.LogWarning("{Owner} group '{Description}' failed: {Error}", entry.Info.Owner, entry.Info.Description, error);
        Record(entry.Info);
        OnChanged();
    }

    /// <summary>Stores the finished group; a failing write is logged and never stops the queue.</summary>
    private void Record(ActionGroupInfo info)
    {
        if (history is null)
            return;
        try
        {
            history.Add(client.Universe.Name, info);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Storing the history of {Owner} group '{Description}' failed", info.Owner, info.Description);
        }
    }

    private void OnChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An action queue listener failed");
        }
    }

    private sealed class Entry(ActionGroupInfo info, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        public ActionGroupInfo Info { get; set; } = info;

        public Func<CancellationToken, Task> Work { get; } = work;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
