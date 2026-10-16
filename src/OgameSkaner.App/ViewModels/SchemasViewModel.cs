using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Schemas;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// Build schemas: a library of build orders (shared by all universes), one schema per planet, and a runner that
/// feeds them into the game as a virtual queue. Everything is saved on every change.
/// </summary>
public sealed partial class SchemasViewModel : PageViewModel, IRecipient<SchemaLibraryChangedMessage>
{
    private const string ActivityName = "The Schemas runner";

    private readonly IUniverseContext _context;
    private readonly ISchemaStore _store;
    private readonly SchemaRunner _runner;
    private readonly OwnPlanetService _ownPlanets;
    private readonly IDialogService _dialogs;
    private readonly AutomationActivity _activity;
    private SchemaRunSettings _settings = new();
    private string? _loadedUniverse;
    private bool _loading;

    public SchemasViewModel(
        IUniverseContext context,
        ISchemaStore store,
        SchemaRunner runner,
        OwnPlanetService ownPlanets,
        IDialogService dialogs,
        AutomationActivity activity,
        IMessenger messenger)
    {
        _context = context;
        _store = store;
        _runner = runner;
        _ownPlanets = ownPlanets;
        _dialogs = dialogs;
        _activity = activity;

        foreach (var schema in store.LoadLibrary())
            Schemas.Add(new SchemaItemViewModel(schema, SaveLibrary));
        SelectedSchema = Schemas.FirstOrDefault();
        messenger.Register(this);

        _context.CurrentChanged += (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            LoadUniverse();
        };
    }

    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = [];

    /// <summary>Buttons of the building palette; a click appends the building's next level.</summary>
    public IReadOnlyList<BuildingDefinition> Palette { get; } = BuildingCatalog.All;

    public ObservableCollection<SchemaPlanetItemViewModel> Planets { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSchemaCommand), nameof(DeleteSchemaCommand))]
    private SchemaItemViewModel? _selectedSchema;

    [ObservableProperty]
    private SchemaStepItemViewModel? _selectedStep;

    [ObservableProperty]
    private SchemaPlanetItemViewModel? _viewedPlanet;

    [ObservableProperty]
    private string? _viewedPlanetInfo;

    [ObservableProperty]
    private string? _planetsInfo;

    [ObservableProperty]
    private int _checkIntervalMinutes = 2;

    [ObservableProperty]
    private string? _statusText;

    public override Task OnNavigatedToAsync()
    {
        if (_context.Current?.Name != _loadedUniverse)
            LoadUniverse();
        return Task.CompletedTask;
    }

    partial void OnSelectedSchemaChanged(SchemaItemViewModel? value)
    {
        foreach (var schema in Schemas)
            schema.ShowProgress(null);
        SaveSettings();
    }

    partial void OnCheckIntervalMinutesChanged(int value) => SaveSettings();

    partial void OnViewedPlanetChanged(SchemaPlanetItemViewModel? value) => _ = ShowViewedPlanetAsync();

    // ---------- library ----------

    [RelayCommand]
    private void NewSchema() => AddSchema(new BuildSchema { Name = $"Schema {Schemas.Count + 1}" });

    [RelayCommand(CanExecute = nameof(HasSelectedSchema))]
    private void DuplicateSchema() => AddSchema(SelectedSchema!.Model.Duplicate(SelectedSchema.Name + " (copy)"));

    [RelayCommand(CanExecute = nameof(HasSelectedSchema))]
    private void DeleteSchema()
    {
        var schema = SelectedSchema!;
        var users = Planets.Count(p => p.Schema == schema);
        var question = users > 0
            ? $"Delete '{schema.Name}'? {users} planet(s) use it and will have no schema."
            : $"Delete '{schema.Name}'?";
        if (!_dialogs.Confirm(question))
            return;

        foreach (var planet in Planets.Where(p => p.Schema == schema))
            planet.Schema = null;
        Schemas.Remove(schema);
        SelectedSchema = Schemas.FirstOrDefault();
        SaveLibrary();
    }

    private bool HasSelectedSchema() => SelectedSchema is not null;

    /// <summary>Another page (the Optimizer) saved a schema into the library: show it and select it.</summary>
    public void Receive(SchemaLibraryChangedMessage message)
    {
        var known = Schemas.Select(s => s.Model.Id).ToHashSet();
        foreach (var schema in _store.LoadLibrary().Where(s => !known.Contains(s.Id)))
            Schemas.Add(new SchemaItemViewModel(schema, SaveLibrary));
        SelectedSchema = Schemas.FirstOrDefault(s => s.Model.Id == message.SchemaId) ?? SelectedSchema;
    }

    private void AddSchema(BuildSchema schema)
    {
        var item = new SchemaItemViewModel(schema, SaveLibrary);
        Schemas.Add(item);
        SelectedSchema = item;
        SaveLibrary();
    }

    /// <summary>Inserts the building just below the selected step (at the end when none is selected) and selects it.</summary>
    [RelayCommand]
    private void AddStep(BuildingType building)
    {
        if (SelectedSchema is { } schema)
            SelectedStep = schema.AddStep(building, SelectedStep is { } selected ? selected.Index + 1 : schema.Steps.Count);
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedSchema is { } schema && SelectedStep is { } step)
        {
            schema.RemoveStep(step.Index);
            SelectedStep = schema.Steps.ElementAtOrDefault(Math.Min(step.Index, schema.Steps.Count - 1));
        }
    }

    [RelayCommand]
    private void MoveStepUp() => MoveStep(-1);

    [RelayCommand]
    private void MoveStepDown() => MoveStep(1);

    private void MoveStep(int delta)
    {
        if (SelectedSchema is not { } schema || SelectedStep is not { } step)
            return;

        schema.MoveStep(step.Index, delta);
        SelectedStep = schema.Steps.ElementAtOrDefault(Math.Clamp(step.Index + delta, 0, schema.Steps.Count - 1));
    }

    // ---------- planets & runner ----------

    [RelayCommand]
    private async Task RefreshPlanetsAsync()
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        try
        {
            ShowPlanets((await _ownPlanets.RefreshAsync(client)).List);
            AddLog($"Planet list refreshed: {Planets.Count} planets");
        }
        catch (Exception ex) when (ex is OgamexException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Refreshing planets failed: {ex.Message}");
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var jobs = Planets.Where(p => p.IsEnabled && p.Schema is not null)
            .Select(p => new SchemaJob(p.Planet.Id, p.Schema!.Model))
            .ToList();
        if (jobs.Count == 0)
        {
            _dialogs.ShowError("Tick at least one planet and choose its schema.");
            return;
        }

        if (!_activity.TryStart(ActivityName))
        {
            _dialogs.ShowError($"{_activity.Running} is running and feeds the same build queue. Stop it first.");
            return;
        }

        foreach (var planet in Planets)
            planet.Status = jobs.Any(j => j.Planet == planet.Planet.Id) ? "starting…" : null;

        var progress = new Progress<SchemaProgress>(p =>
        {
            AddLog(p.Message);
            if (p.Planet is { } id && p.Status is { } status && Planets.FirstOrDefault(x => x.Planet.Id == id) is { } planet)
                planet.Status = status;
            StatusText = $"Running… {p.TotalPushed} items pushed";
        });

        SchemaRunResult result;
        try
        {
            AddLog($"Schemas started on {string.Join(", ", Planets.Where(p => jobs.Any(j => j.Planet == p.Planet.Id)).Select(p => p.Name))}");
            result = await _runner.RunAsync(client, jobs, TimeSpan.FromMinutes(Math.Max(1, CheckIntervalMinutes)), progress, cancellationToken);
        }
        finally
        {
            _activity.Stop(ActivityName);
        }

        StatusText = result.Outcome switch
        {
            SchemaRunOutcome.Finished => $"Finished: every schema is built or queued ({result.Pushed} items pushed).",
            SchemaRunOutcome.Cancelled => $"Stopped after {result.Pushed} pushed items.",
            _ => $"Failed after {result.Pushed} pushed items."
        };
        AddLog(StatusText);
        if (result.Outcome == SchemaRunOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "The schema run failed.");
        if (ViewedPlanet is not null)
            await ShowViewedPlanetAsync();
    }

    [RelayCommand]
    private Task RefreshViewedPlanetAsync() => ShowViewedPlanetAsync();

    /// <summary>Reads the clicked planet, selects its schema and marks the steps built, queued and next.</summary>
    private async Task ShowViewedPlanetAsync()
    {
        ViewedPlanetInfo = null;
        if (ViewedPlanet is not { } viewed || _context.Client is not { } client)
            return;

        if (viewed.Schema is { } schema)
            SelectedSchema = schema;

        if (RunCommand.IsRunning)
        {
            ViewedPlanetInfo = $"{viewed.Name}: progress is shown when the run has finished (see the status column meanwhile).";
            return;
        }

        ViewedPlanetInfo = $"Reading {viewed.Planet}…";
        try
        {
            var state = await _ownPlanets.InspectAsync(client, viewed.Planet.Id);
            if (ViewedPlanet != viewed)
                return;

            if (viewed.Schema is not { } planetSchema)
            {
                ViewedPlanetInfo = $"{viewed.Planet}: no schema assigned.";
                return;
            }

            planetSchema.ShowProgress(state);
            var done = planetSchema.Steps.Count(s => s.Mark == "✓");
            var next = SchemaPlanner.Next(state, planetSchema.Model);
            ViewedPlanetInfo = $"{viewed.Planet}: {done}/{planetSchema.Steps.Count} steps built, {state.Queue.Count} in the game's queue. " +
                               (next is SchemaAction.Upgrade u ? $"Next: {BuildingCatalog.Get(u.Building).DisplayName} {u.Level} ({u.Reason})." : $"Next: {next.Reason}.");
        }
        catch (OgamexException ex)
        {
            ViewedPlanetInfo = $"Reading {viewed.Planet} failed: {ex.Message}";
        }
    }

    // ---------- loading & saving ----------

    private void LoadUniverse()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            _settings = _loadedUniverse is null ? new SchemaRunSettings() : _store.LoadSettings(_loadedUniverse);
            CheckIntervalMinutes = _settings.CheckIntervalMinutes;
            if (Schemas.FirstOrDefault(s => s.Model.Id == _settings.SelectedSchemaId) is { } selected)
                SelectedSchema = selected;
            ShowPlanets(_loadedUniverse is null ? OwnPlanetList.Empty : _ownPlanets.GetStored(_loadedUniverse));
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowPlanets(OwnPlanetList list)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            Planets.Clear();
            foreach (var planet in list.Planets)
            {
                var assignment = _settings.Planets.FirstOrDefault(a => a.PlanetId == planet.Id.Value);
                var schema = Schemas.FirstOrDefault(s => s.Model.Id == assignment?.SchemaId);
                Planets.Add(new SchemaPlanetItemViewModel(planet, assignment?.Enabled ?? false, schema, SaveSettings));
            }
        }
        finally
        {
            _loading = wasLoading;
        }

        PlanetsInfo = list.RefreshedAt is { } at
            ? $"{Planets.Count} planets, refreshed {at:g}"
            : "Planet list not refreshed yet";
    }

    private void SaveLibrary()
    {
        try
        {
            _store.SaveLibrary(Schemas.Select(s => s.Model));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the schemas failed: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        if (_loading || _loadedUniverse is null)
            return;

        // Keep assignments of planets that are not in the current list.
        var shown = Planets.Select(p => p.Planet.Id.Value).ToHashSet();
        _settings.Planets = _settings.Planets.Where(a => !shown.Contains(a.PlanetId))
            .Concat(Planets.Select(p => new SchemaAssignment { PlanetId = p.Planet.Id.Value, SchemaId = p.Schema?.Model.Id, Enabled = p.IsEnabled }))
            .ToList();
        _settings.CheckIntervalMinutes = Math.Max(1, CheckIntervalMinutes);
        _settings.SelectedSchemaId = SelectedSchema?.Model.Id;

        try
        {
            _store.SaveSettings(_loadedUniverse, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the schema settings failed: {ex.Message}");
        }
    }

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}
