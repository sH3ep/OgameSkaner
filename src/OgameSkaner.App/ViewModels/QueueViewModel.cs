using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.App.ViewModels;

/// <summary>One group of the action queue as a grid row.</summary>
public sealed record QueueRow(ActionGroupState State, string Owner, string Description, string Priority, string Queued, string Started, string Duration, string? Error);

/// <summary>
/// The action queue of the selected universe: what runs, what waits (in the order it will run) and what ran lately.
/// Every module acts in the game only through this queue, one group at a time.
/// </summary>
public sealed partial class QueueViewModel : PageViewModel
{
    private readonly ActionQueueMonitor _monitor;

    public QueueViewModel(ActionQueueMonitor monitor)
    {
        _monitor = monitor;
        _monitor.PropertyChanged += OnMonitorChanged;
        Update();

        // The waiting and running times grow by the second.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Update();
        timer.Start();
    }

    /// <summary>The running groups first, then the waiting ones in the order they will run.</summary>
    [ObservableProperty]
    private IReadOnlyList<QueueRow> _current = [];

    /// <summary>The latest finished groups, newest first.</summary>
    [ObservableProperty]
    private IReadOnlyList<QueueRow> _finished = [];

    [ObservableProperty]
    private string _summary = "";

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionQueueMonitor.Snapshot))
            Update();
    }

    private void Update()
    {
        var snapshot = _monitor.Snapshot;
        var now = DateTimeOffset.Now;
        // New lists only when something changed, so the grids keep their scroll position and selection.
        var current = snapshot.Running.Concat(snapshot.Waiting).Select(g => Row(g, now)).ToList();
        if (!current.SequenceEqual(Current))
            Current = current;
        var finished = snapshot.Finished.Select(g => Row(g, now)).ToList();
        if (!finished.SequenceEqual(Finished))
            Finished = finished;

        Summary = snapshot.Running.Count == 0 && snapshot.Waiting.Count == 0
            ? "Idle: no module is acting in the game."
            : $"{snapshot.Running.Count} running, {snapshot.Waiting.Count} waiting"
              + (snapshot.HomePlanet is { } home ? $"; the game returns to planet {home} when idle" : "");
    }

    private static QueueRow Row(ActionGroupInfo group, DateTimeOffset now)
    {
        var duration = group.StartedAt is { } started
            ? (group.FinishedAt ?? now) - started
            : now - group.QueuedAt;
        return new QueueRow(
            group.State,
            group.Owner,
            group.Description,
            group.Priority switch
            {
                ActionPriority.User => "Player",
                ActionPriority.Urgent => "Urgent",
                _ => "Automation"
            },
            $"{group.QueuedAt:T}",
            group.StartedAt is { } at ? $"{at:T}" : "",
            group.State == ActionGroupState.Waiting ? $"waiting {Format(duration)}" : Format(duration),
            group.Error);
    }

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span:mm\\:ss}" : span.TotalMinutes >= 1 ? $"{span:m\\:ss}" : $"{span.TotalSeconds:0.0} s";
}
