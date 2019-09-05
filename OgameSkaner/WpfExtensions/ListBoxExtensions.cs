using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OgameSkaner.WpfExtensions
{
    internal class ListBoxExtensions
    {
        public static readonly DependencyProperty ScrollChangedCommandProperty = DependencyProperty.RegisterAttached(
            "ScrollChangedCommand", typeof(ICommand), typeof(ListBoxExtensions),
            new PropertyMetadata(default(ICommand), OnScrollChangedCommandChanged));

        private static void OnScrollChangedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var listBox = d as ListBox;
            if (listBox == null)
                return;
            if (e.NewValue != null)
                listBox.Loaded += ListBoxOnLoaded;
            else if (e.OldValue != null) listBox.Loaded -= ListBoxOnLoaded;
        }

        private static void ListBoxOnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            var listBox = sender as ListBox;
            if (listBox == null)
                return;

            var scrollViewer = UIHelper.FindChildren<ScrollViewer>(listBox).FirstOrDefault();
            if (scrollViewer != null) scrollViewer.ScrollChanged += ScrollViewerOnScrollChanged;
        }

        private static void ScrollViewerOnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var dataGrid = UIHelper.FindParent<ListBox>(sender as ScrollViewer);
            if (dataGrid != null)
            {
                var command = GetScrollChangedCommand(dataGrid);
                command.Execute(e);
            }
        }

        public static void SetScrollChangedCommand(DependencyObject element, ICommand value)
        {
            element.SetValue(ScrollChangedCommandProperty, value);
        }

        public static ICommand GetScrollChangedCommand(DependencyObject element)
        {
            return (ICommand) element.GetValue(ScrollChangedCommandProperty);
        }
    }
}