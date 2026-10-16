namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// Which build automation is running. The Builder and the Schemas runner both feed the game's build queue, so only
/// one of them may run at a time.
/// </summary>
public sealed class AutomationActivity
{
    private readonly object _sync = new();

    public string? Running { get; private set; }

    /// <returns>False (and nothing changes) when another automation is running.</returns>
    public bool TryStart(string name)
    {
        lock (_sync)
        {
            if (Running is not null && Running != name)
                return false;
            Running = name;
            return true;
        }
    }

    public void Stop(string name)
    {
        lock (_sync)
        {
            if (Running == name)
                Running = null;
        }
    }
}
