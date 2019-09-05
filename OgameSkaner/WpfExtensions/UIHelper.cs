using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OgameSkaner.WpfExtensions
{
    internal static class UIHelper
    {
        internal static IList<T> FindChildren<T>(DependencyObject element) where T : FrameworkElement
        {
            var retval = new List<T>();
            for (var counter = 0; counter < VisualTreeHelper.GetChildrenCount(element); counter++)
            {
                var toadd = VisualTreeHelper.GetChild(element, counter) as FrameworkElement;
                if (toadd != null)
                {
                    var correctlyTyped = toadd as T;
                    if (correctlyTyped != null)
                        retval.Add(correctlyTyped);
                    else
                        retval.AddRange(FindChildren<T>(toadd));
                }
            }

            return retval;
        }

        internal static T FindParent<T>(DependencyObject element) where T : FrameworkElement
        {
            var parent = VisualTreeHelper.GetParent(element) as FrameworkElement;
            while (parent != null)
            {
                var correctlyTyped = parent as T;
                if (correctlyTyped != null) return correctlyTyped;
                return FindParent<T>(parent);
            }

            return null;
        }
    }
}