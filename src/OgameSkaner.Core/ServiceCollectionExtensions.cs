using Microsoft.Extensions.DependencyInjection;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Legacy;

namespace OgameSkaner.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers storage, the OgameX client factory and the domain services.
    /// The host must also register an <see cref="ISecretProtector"/> and configure <see cref="OgamexOptions"/>.
    /// </summary>
    public static IServiceCollection AddOgameSkanerCore(this IServiceCollection services, IAppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IProfileStore, JsonProfileStore>();
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<IPlanetRepository, JsonPlanetRepository>();
        services.AddSingleton<LegacyImporter>();

        services.AddSingleton<IOgamexClientFactory, OgamexClientFactory>();

        services.AddSingleton<GalaxyScanService>();
        services.AddSingleton<FleetEscapeService>();
        services.AddSingleton<CsvExportService>();

        return services;
    }
}
