using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.Infrastructure;

/// <summary>The universe the user is currently working with, and its client and planet database.</summary>
public interface IUniverseContext
{
    UniverseProfile? Current { get; }

    IOgamexClient? Client { get; }

    /// <summary>Raised on the UI thread after <see cref="Select"/> switched to another universe.</summary>
    event EventHandler? CurrentChanged;

    void Select(UniverseProfile? universe);

    /// <exception cref="InvalidOperationException">No universe is selected.</exception>
    Task<PlanetDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
}

public sealed class UniverseContext(IOgamexClientFactory clientFactory, IPlanetRepository planets) : IUniverseContext, IDisposable
{
    public UniverseProfile? Current { get; private set; }

    public IOgamexClient? Client { get; private set; }

    public event EventHandler? CurrentChanged;

    public void Select(UniverseProfile? universe)
    {
        if (universe == Current)
            return;

        Client?.Dispose();
        Current = universe;
        Client = universe is null ? null : clientFactory.Create(universe);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<PlanetDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default) =>
        Current is { } universe
            ? planets.GetAsync(universe.Name, cancellationToken)
            : throw new InvalidOperationException("No universe selected.");

    public void Dispose() => Client?.Dispose();
}
