using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Views;

public partial class RaidsView : UserControl
{
    /// <summary>The last checkbox clicked without Shift: the start of a Shift+click range, as in Windows Explorer.</summary>
    private RaidTargetItemViewModel? _rangeAnchor;

    public RaidsView() => InitializeComponent();

    /// <summary>Shift+click ticks or unticks every row between the anchor and the clicked one, in the grid's current sort order.</summary>
    private void OnSelectCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: RaidTargetItemViewModel item } checkBox)
            return;

        if (_rangeAnchor is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var rows = TargetsGrid.Items.OfType<RaidTargetItemViewModel>().ToList();
            var from = rows.IndexOf(_rangeAnchor);
            var to = rows.IndexOf(item);
            if (from >= 0 && to >= 0)
            {
                var isChecked = checkBox.IsChecked == true;
                for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                    rows[i].IsSelected = isChecked;
                return;
            }
        }

        // A refresh replaces the rows, so a stale anchor simply starts a new range here.
        _rangeAnchor = item;
    }
}
