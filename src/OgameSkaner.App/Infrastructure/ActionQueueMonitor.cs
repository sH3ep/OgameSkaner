using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// The selected universe's <see cref="IActionQueue"/> for the UI: its latest snapshot, refreshed on the UI thread when
/// the queue changes (several changes in a row are shown once).
/// </summary>
public sealed partial class ActionQueueMonitor : ObservableObject
{
    private static readonly ActionQueueSnapshot Empty = new([], [], [], null);

    private readonly IUniverseContext _context;
    private readonly IActionQueues _queues;
    private IActionQueue? _queue;
    private int _refreshPending;

    public ActionQueueMonitor(IUniverseContext context, IActionQueues queues)
    {
        _context = context;
        _queues = queues;
        _context.CurrentChanged += (_, _) => Attach();
        Attach();
    }

    [ObservableProperty]
    private ActionQueueSnapshot _snapshot = Empty;

    /// <summary>What the queue does now, for the sidebar: the running group and how many wait.</summary>
    [ObservableProperty]
    private string _summary = "Idle";

    private void Attach()
    {
        if (_queue is not null)
            _queue.Changed -= OnChanged;
        _queue = _context.Client is { } client ? _queues.For(client) : null;
        if (_queue is not null)
            _queue.Changed += OnChanged;
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
            return;
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        var snapshot = _queue?.Snapshot() ?? Empty;
        Snapshot = snapshot;

        var running = snapshot.Running.FirstOrDefault(g => g.Priority != ActionPriority.Urgent) ?? snapshot.Running.FirstOrDefault();
        var waiting = snapshot.Waiting.Count > 0 ? $"{snapshot.Waiting.Count} waiting" : null;
        Summary = running is null
            ? waiting ?? "Idle"
            : $"{running.Owner}: {running.Description}" + (waiting is null ? "" : $"{Environment.NewLine}{waiting}");
    }
}
