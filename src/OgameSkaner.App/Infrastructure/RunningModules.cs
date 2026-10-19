using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// Which automations run right now, for the sidebar: each page hands over its long-running commands, and while one of
/// them runs the page's <see cref="PageDescriptor.IsRunning"/> is set (the green dot next to its title).
/// </summary>
public sealed class RunningModules(IEnumerable<PageDescriptor> pages)
{
    private readonly IReadOnlyList<PageDescriptor> _pages = pages.ToList();
    private readonly Dictionary<PageDescriptor, List<IAsyncRelayCommand>> _commands = [];

    /// <summary>Tracks <paramref name="command"/> for the page of <paramref name="pageType"/> (a tab's view model passes its page's type).</summary>
    public void Track(Type pageType, IAsyncRelayCommand command)
    {
        if (_pages.FirstOrDefault(p => p.ViewModelType == pageType) is not { } descriptor)
            return;

        if (!_commands.TryGetValue(descriptor, out var commands))
            _commands[descriptor] = commands = [];
        commands.Add(command);
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
                descriptor.IsRunning = commands.Any(c => c.IsRunning);
        };
    }

    public void Track(PageViewModel page, IAsyncRelayCommand command) => Track(page.GetType(), command);
}
