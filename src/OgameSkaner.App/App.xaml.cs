using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.App.ViewModels;
using OgameSkaner.App.Views;
using OgameSkaner.Core;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Legacy;
using Serilog;

namespace OgameSkaner.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = BuildHost(e.Args);
        DispatcherUnhandledException += OnUnhandledException;

        PageRegistration.RegisterTemplates(Resources, _host.Services.GetServices<PageDescriptor>());

        var importMessage = await ImportLegacyDataOnFirstRunAsync(_host.Services);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        if (importMessage is not null)
            _host.Services.GetRequiredService<IDialogService>().ShowInfo(importMessage);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private static IHost BuildHost(string[] args)
    {
        var paths = AppPaths.Default;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Optional per-user overrides, e.g. to enable Ogamex:DumpResponsesDirectory.
        builder.Configuration.AddJsonFile(System.IO.Path.Combine(paths.Root, "appsettings.json"), optional: true);

        builder.Services.AddSerilog(logger => logger
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.File(System.IO.Path.Combine(paths.LogsDirectory, "ogameskaner-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));

        builder.Services.Configure<OgamexOptions>(builder.Configuration.GetSection(OgamexOptions.SectionName));
        builder.Services.AddOgameSkanerCore(paths);

        builder.Services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IUniverseContext, UniverseContext>();
        builder.Services.AddSingleton<AutomationActivity>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<ProfilesViewModel>();
        builder.Services.AddTransient<ProfilesWindow>();

        builder.Services.AddPage<ScannerViewModel, ScannerView>("Scanner");
        builder.Services.AddPage<PlayersViewModel, PlayersView>("Players");
        builder.Services.AddPage<BuilderViewModel, BuilderView>("Builder");
        builder.Services.AddPage<SchemasViewModel, SchemasView>("Schemas");
        builder.Services.AddPage<OptimizerViewModel, OptimizerView>("Optimizer");
        builder.Services.AddPage<RaidsViewModel, RaidsView>("Raids");
        builder.Services.AddPage<UtilitiesViewModel, UtilitiesView>("Utilities");

        return builder.Build();
    }

    /// <summary>On the very first start, picks up GamesConfiguration.xml from the old version if it sits next to the app.</summary>
    private static async Task<string?> ImportLegacyDataOnFirstRunAsync(IServiceProvider services)
    {
        if (services.GetRequiredService<IProfileStore>().Exists)
            return null;

        var folder = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .FirstOrDefault(LegacyImporter.ContainsLegacyData);
        if (folder is null)
            return null;

        try
        {
            var result = await services.GetRequiredService<LegacyImporter>().ImportAsync(folder);
            return ProfilesViewModel.DescribeImport(result);
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILogger<App>>().LogError(ex, "Legacy import from {Folder} failed", folder);
            return $"Importing data from the old version failed: {ex.Message}";
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception");
        MessageBox.Show(e.Exception.Message, "OgameSkaner – unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
