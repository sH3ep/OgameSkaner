using System.Globalization;
using System.Windows.Data;

namespace OgameSkaner.App.Converters;

/// <summary>Binds a RadioButton to an enum value: <c>ConverterParameter</c> is the value the button stands for.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name && value.ToString() == name;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
