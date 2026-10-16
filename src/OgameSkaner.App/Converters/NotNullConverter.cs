using System.Globalization;
using System.Windows.Data;

namespace OgameSkaner.App.Converters;

/// <summary>True when the value is not null (e.g. to enable an editor only while something is selected).</summary>
public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
