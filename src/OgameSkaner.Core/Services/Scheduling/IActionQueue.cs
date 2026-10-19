using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;

namespace OgameSkaner.Core.Services.Scheduling;

public enum ActionPriority
{
    /// <summary>A module running on its own (Builder, Raids, …).</summary>
    Automation,

    /// <summary>A button the player clicked: runs before the waiting automation groups.</summary>
    User,

    /// <summary>Not queued at all (<see cref="IActionQueue.RunUrgentAsync{T}"/>): the attack protection's escapes.</summary>
    Urgent
}

public enum ActionGroupState
{
    Waiting,
    Running,
    Done,
    Failed,
    Cancelled
}

/// <summary>One group of actions as the Queue page shows it.</summary>
public sealed record ActionGroupInfo(long Id, string Owner, string Description, ActionPriority Priority, DateTimeOffset QueuedAt)
{
    public ActionGroupState State { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? Error { get; init; }
}

/// <param name="Finished">The latest finished groups, newest first.</param>
/// <param name="HomePlanet">The planet the game returns to when the queue is idle; null while idle or unknown.</param>
public sealed record ActionQueueSnapshot(
    IReadOnlyList<ActionGroupInfo> Running,
    IReadOnlyList<ActionGroupInfo> Waiting,
    IReadOnlyList<ActionGroupInfo> Finished,
    PlanetId? HomePlanet);

/// <summary>
/// The one place where a universe's modules act in the game. A module hands over a <b>group</b> of actions (queue the
/// next buildings of one planet, spy on 20 targets, send the due expeditions) and awaits it; the queue runs one group at
/// a time with a random pause between groups, so modules never switch the game's current planet under each other and
/// the account behaves like one player doing one thing after another.
/// </summary>
/// <remarks>
/// A group should only talk to the game: planning, optimizer runs and waits belong between groups, since they would hold
/// up every other module. While the queue is busy it remembers the planet that was selected in the game before the first
/// group and selects it again once nothing ran for a while.
/// </remarks>
public interface IActionQueue
{
    /// <summary>The planet selected in the game before the queue got busy; null while it's idle (or unknown).</summary>
    PlanetId? HomePlanet { get; }

    /// <summary>
    /// Queues <paramref name="work"/> and returns its result once it ran. Cancelling while the group waits takes it off
    /// the queue; while it runs, <paramref name="work"/> gets the token.
    /// </summary>
    Task<T> RunAsync<T>(
        string owner,
        string description,
        Func<CancellationToken, Task<T>> work,
        ActionPriority priority = ActionPriority.Automation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> at once, next to a running group: only for the fleet escape, which can't wait. Its
    /// requests name their planet (<see cref="IOgamexClient"/> switches back for the group's next planet-bound request).
    /// </summary>
    Task<T> RunUrgentAsync<T>(string owner, string description, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    ActionQueueSnapshot Snapshot();

    /// <summary>Raised (on a background thread) when a group is queued, starts, finishes or the home planet changes.</summary>
    event EventHandler? Changed;
}

/// <summary>The queue of each universe's client.</summary>
public interface IActionQueues
{
    IActionQueue For(IOgamexClient client);
}

public static class ActionQueueExtensions
{
    public static Task RunAsync(
        this IActionQueue queue,
        string owner,
        string description,
        Func<CancellationToken, Task> work,
        ActionPriority priority = ActionPriority.Automation,
        CancellationToken cancellationToken = default) =>
        queue.RunAsync(owner, description, async token =>
        {
            await work(token).ConfigureAwait(false);
            return true;
        }, priority, cancellationToken);

    public static Task RunUrgentAsync(this IActionQueue queue, string owner, string description, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        queue.RunUrgentAsync(owner, description, async token =>
        {
            await work(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
}
