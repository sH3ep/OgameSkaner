using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// Which automations run right now, for the sidebar: each page hands over its long-running command, and while it runs
/// the page's <see cref="PageDescriptor.IsRunning"/> is set and its title is in <see cref="Titles"/> (menu order).
/// </summary>
public sealed class RunningModules(IEnumerable<PageDescriptor> pages)
{
    private readonly IReadOnlyList<PageDescriptor> _pages = pages.ToList();

    public ObservableCollection<string> Titles { get; } = [];

    public void Track(PageViewModel page, IAsyncRelayCommand command)
    {
        if (_pages.FirstOrDefault(p => p.ViewModelType == page.GetType()) is not { } descriptor)
            return;

        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IAsyncRelayCommand.IsRunning) || descriptor.IsRunning == command.IsRunning)
                return;
            descriptor.IsRunning = command.IsRunning;
            Titles.Clear();
            foreach (var running in _pages.Where(p => p.IsRunning))
                Titles.Add(running.Title);
        };
    }
}
