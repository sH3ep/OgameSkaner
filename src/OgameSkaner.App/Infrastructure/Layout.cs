using System.Windows;
using System.Windows.Controls;

namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// <c>Layout.Key="Page.Part"</c> on a window, a grid with splitters or a data grid remembers its sizes in
/// <see cref="LayoutStore"/>: restored when it loads, saved when it unloads (a page is rebuilt on every visit) and when
/// the main window closes. A stored layout whose column or row count no longer matches the XAML is ignored.
/// </summary>
public static class Layout
{
    /// <summary>Set by the app at startup; without it (the designer) nothing is remembered.</summary>
    public static LayoutStore? Store { get; set; }

    /// <summary>The grids and data grids on screen, saved too when the app closes (they don't unload then).</summary>
    private static readonly HashSet<FrameworkElement> Loaded = [];

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Layout), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    /// <summary>Saves everything on screen and writes the file.</summary>
    public static void SaveAll()
    {
        foreach (var element in Loaded.ToList())
            Capture(element);
        Store?.Save();
    }

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case Window window when e.OldValue is null:
                window.SourceInitialized += (_, _) => RestoreWindow(window);
                window.Closing += (_, _) =>
                {
                    CaptureWindow(window);
                    SaveAll();
                };
                break;
            case Grid or DataGrid when e.OldValue is null:
                var element = (FrameworkElement)d;
                element.Loaded += (_, _) =>
                {
                    Loaded.Add(element);
                    Restore(element);
                };
                element.Unloaded += (_, _) =>
                {
                    Loaded.Remove(element);
                    Capture(element);
                    Store?.Save();
                };
                break;
        }
    }

    private static void Restore(FrameworkElement element)
    {
        if (Store is not { } store || GetKey(element) is not { } key)
            return;

        if (element is Grid grid && store.Data.Grids.TryGetValue(key, out var layout)
            && layout.Columns.Count == grid.ColumnDefinitions.Count && layout.Rows.Count == grid.RowDefinitions.Count)
        {
            for (var i = 0; i < layout.Columns.Count; i++)
                grid.ColumnDefinitions[i].Width = ToGridLength(layout.Columns[i]);
            for (var i = 0; i < layout.Rows.Count; i++)
                grid.RowDefinitions[i].Height = ToGridLength(layout.Rows[i]);
        }
        else if (element is DataGrid dataGrid && store.Data.DataGrids.TryGetValue(key, out var columns) && columns.Count == dataGrid.Columns.Count)
        {
            for (var i = 0; i < columns.Count; i++)
                dataGrid.Columns[i].Width = new DataGridLength(columns[i].Width.Value, (DataGridLengthUnitType)columns[i].Width.Unit);
            // Display indexes must stay a permutation while they are set, so set them in their final order.
            if (columns.Select(c => c.DisplayIndex).Order().SequenceEqual(Enumerable.Range(0, columns.Count)))
            {
                foreach (var (column, index) in dataGrid.Columns.Zip(columns.Select(c => c.DisplayIndex)).OrderBy(p => p.Second).ToList())
                    column.DisplayIndex = index;
            }
        }
    }

    private static void Capture(FrameworkElement element)
    {
        if (Store is not { } store || GetKey(element) is not { } key)
            return;

        if (element is Grid grid)
        {
            store.Data.Grids[key] = new GridLayout(
                grid.ColumnDefinitions.Select(c => FromGridLength(c.Width)).ToList(),
                grid.RowDefinitions.Select(r => FromGridLength(r.Height)).ToList());
        }
        else if (element is DataGrid dataGrid)
        {
            store.Data.DataGrids[key] = dataGrid.Columns
                .Select(c => new DataGridColumnLayout(new LengthLayout(c.Width.Value, (int)c.Width.UnitType), c.DisplayIndex))
                .ToList();
        }
    }

    private static void RestoreWindow(Window window)
    {
        if (Store is not { } store || GetKey(window) is not { } key || !store.Data.Windows.TryGetValue(key, out var layout))
            return;

        // Only where it is still on a screen (a monitor may be gone).
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(layout.Left, layout.Top, Math.Max(window.MinWidth, layout.Width), Math.Max(window.MinHeight, layout.Height));
        if (!screen.IntersectsWith(bounds))
            return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        if (layout.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    private static void CaptureWindow(Window window)
    {
        if (Store is not { } store || GetKey(window) is not { } key)
            return;

        var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        if (bounds.IsEmpty)
            return;
        store.Data.Windows[key] = new WindowLayout(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
    }

    private static LengthLayout FromGridLength(GridLength length) => new(length.Value, (int)length.GridUnitType);

    private static GridLength ToGridLength(LengthLayout length) => new(length.Value, (GridUnitType)length.Unit);
}
