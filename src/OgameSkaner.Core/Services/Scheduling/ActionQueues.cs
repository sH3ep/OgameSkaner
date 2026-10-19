using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Scheduling;

/// <summary>One <see cref="ActionQueue"/> per client (a new client after a universe change gets a new queue).</summary>
public sealed class ActionQueues(ActionQueueSettings settings, TimeProvider timeProvider, ILoggerFactory loggerFactory, IActionHistoryStore? history = null) : IActionQueues
{
    private readonly ConditionalWeakTable<IOgamexClient, ActionQueue> _queues = new();

    public ActionQueues(IOptions<OgamexOptions> options, TimeProvider timeProvider, ILoggerFactory loggerFactory, IActionHistoryStore history)
        : this(ActionQueueSettings.From(options.Value), timeProvider, loggerFactory, history)
    {
    }

    public IActionQueue For(IOgamexClient client) =>
        _queues.GetValue(client, c => new ActionQueue(c, settings, timeProvider, loggerFactory.CreateLogger<ActionQueue>(), history));
}
