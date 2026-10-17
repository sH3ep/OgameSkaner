using Microsoft.Extensions.DependencyInjection;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Optimizer;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Services.Schemas;
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
        services.AddSingleton<IBuildSettingsStore, JsonBuildSettingsStore>();
        services.AddSingleton<IOwnPlanetStore, JsonOwnPlanetStore>();
        services.AddSingleton<ISchemaStore, JsonSchemaStore>();
        services.AddSingleton<IOptimizerResultStore, JsonOptimizerResultStore>();
        services.AddSingleton<IRaidStore, JsonRaidStore>();
        services.AddSingleton<LegacyImporter>();

        services.AddSingleton<IOgamexClientFactory, OgamexClientFactory>();

        services.AddSingleton<GalaxyScanService>();
        services.AddSingleton<FleetEscapeService>();
        services.AddSingleton<CsvExportService>();
        services.AddSingleton<OwnPlanetService>();
        services.AddSingleton<SchemaRunner>();
        services.AddSingleton<SimulatedBuildRunner>();
        services.AddSingleton<BuildOrderOptimizer>();
        services.AddSingleton<RaidRunner>();

        return services;
    }
}
