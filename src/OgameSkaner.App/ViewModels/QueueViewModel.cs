using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.App.ViewModels;

/// <summary>A module's fleet slot reservation or claim as a grid row.</summary>
/// <param name="HeldNow">Slots kept free for it now, which the other modules count as used.</param>
/// <param name="Total">A reservation's slots (in flight included) or a claim's slots.</param>
public sealed record SlotRow(string Owner, string Kind, int HeldNow, int Total, string InFlight, string NextBack, string Since, string Until);

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

    /// <summary>The fleet slot reservations first, then the claims in the order they are served.</summary>
    [ObservableProperty]
    private IReadOnlyList<SlotRow> _slotRows = [];

    /// <summary>The fleet slots as last read and how many are left for the modules without a reservation or claim.</summary>
    [ObservableProperty]
    private string _slotsSummary = "";

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ActionQueueMonitor.Snapshot) or nameof(ActionQueueMonitor.ClaimsText))
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
        UpdateFleetSlots(now);
    }

    private void UpdateFleetSlots(DateTimeOffset now)
    {
        var (claims, reading) = _monitor.ReadFleetSlots();
        var rows = claims.Select(c => SlotRow(c, now)).ToList();
        if (!rows.SequenceEqual(SlotRows))
            SlotRows = rows;

        var held = claims.Sum(c => c.Count);
        var parts = new List<string>();
        if (reading is { } read)
        {
            var free = read.Slots.Free;
            parts.Add($"{read.Slots.Used}/{read.Slots.Max} in use as {read.Owner} read them {Format(now - read.At)} ago");
            parts.Add(held == 0
                ? $"{free} free for every module"
                : $"{free} free, {Math.Min(free, held)} of them held → {Math.Max(0, free - held)} for the modules without a reservation or claim");
        }
        else
            parts.Add("not read yet (no module that respects reservations has looked at them)");

        if (claims.Count == 0)
            parts.Add("no reservations or claims");
        SlotsSummary = "Fleet slots: " + string.Join("; ", parts) + ".";
    }

    private static SlotRow SlotRow(FleetSlotClaim claim, DateTimeOffset now) => claim.IsReservation
        ? new SlotRow(claim.Owner, "Reservation", claim.Count, claim.Reserved!.Value, claim.InFlight.ToString(),
            claim.NextBackAt is { } back ? $"{back.ToLocalTime():T} (in {Format(back - now)})" : "",
            $"{claim.Since.ToLocalTime():T}", "until stopped")
        : new SlotRow(claim.Owner, "Claim", claim.Count, claim.Count, "", "",
            $"{claim.Since.ToLocalTime():T}", $"{claim.ExpiresAt.ToLocalTime():T} unless renewed");

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
