using Microsoft.Extensions.DependencyInjection;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Services.Alerts;
using OgameSkaner.Core.Services.Asteroids;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Defense;
using OgameSkaner.Core.Services.Expeditions;
using OgameSkaner.Core.Services.Moons;
using OgameSkaner.Core.Services.Optimizer;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Services.Schemas;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Legacy;
using OgameSkaner.Core.Storage.Sqlite;

namespace OgameSkaner.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers storage, the OgameX client factory and the domain services.
    /// The host must also register an <see cref="ISecretProtector"/> and configure <see cref="OgamexOptions"/>; the
    /// <see cref="Domain.Alerts.IAlertChannel"/>s it registers are the ways alerts reach the player.
    /// </summary>
    public static IServiceCollection AddOgameSkanerCore(this IServiceCollection services, IAppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IProfileStore, JsonProfileStore>();
        services.AddSingleton<ISessionStore, JsonSessionStore>();
        services.AddSingleton<UniverseDatabases>();
        services.AddSingleton<IPlanetRepository, SqlitePlanetRepository>();
        services.AddSingleton<IBuildSettingsStore, JsonBuildSettingsStore>();
        services.AddSingleton<IScanSettingsStore, JsonScanSettingsStore>();
        services.AddSingleton<IOwnPlanetStore, JsonOwnPlanetStore>();
        services.AddSingleton<ISchemaStore, JsonSchemaStore>();
        services.AddSingleton<IOptimizerResultStore, JsonOptimizerResultStore>();
        services.AddSingleton<IRaidStore, SqliteRaidStore>();
        services.AddSingleton<IAsteroidStore, SqliteAsteroidStore>();
        services.AddSingleton<IActionHistoryStore, SqliteActionHistoryStore>();
        services.AddSingleton<IDefenseSettingsStore, JsonDefenseSettingsStore>();
        services.AddSingleton<IExpeditionSettingsStore, JsonExpeditionSettingsStore>();
        services.AddSingleton<IMoonSettingsStore, JsonMoonSettingsStore>();
        services.AddSingleton<LegacyImporter>();

        services.AddSingleton<IOgamexClientFactory, OgamexClientFactory>();
        services.AddSingleton<IActionQueues, ActionQueues>();
        services.AddSingleton<IFleetSlotClaimsProvider, FleetSlotClaimsProvider>();

        services.AddSingleton<GalaxyScanService>();
        services.AddSingleton<FleetEscapeService>();
        services.AddSingleton<ColonizationService>();
        services.AddSingleton<ShipyardService>();
        services.AddSingleton<CsvExportService>();
        services.AddSingleton<OwnPlanetService>();
        services.AddSingleton<SchemaRunner>();
        services.AddSingleton<ResourceSharing>();
        services.AddSingleton<SimulatedBuildRunner>();
        services.AddSingleton<BuildOrderOptimizer>();
        services.AddSingleton<RaidRunner>();
        services.AddSingleton<AsteroidRunner>();
        services.AddSingleton<AlertDispatcher>();
        services.AddSingleton<AttackMonitor>();
        services.AddSingleton<ExpeditionRunner>();
        services.AddSingleton<MoonRunner>();

        return services;
    }
}
