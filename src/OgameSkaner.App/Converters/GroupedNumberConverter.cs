using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OgameSkaner.App.Converters;

/// <summary>
/// Whole numbers with thousands separators ("10,000") that can be typed back with or without them; an empty field is
/// null for a nullable number. The default
/// conversion of a <c>StringFormat=N0</c> binding can't read "10,000" back into an int or long. Bind it with
/// <c>UpdateSourceTrigger=LostFocus</c>: reformatting while typing moves the caret and digits end up in the wrong place.
/// </summary>
public sealed class GroupedNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IFormattable number ? number.ToString("N0", culture) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? string.Empty).Replace(" ", "").Replace(" ", "");
        var type = Nullable.GetUnderlyingType(targetType);
        if (text.Length == 0 && type is not null)
            return null!; // an empty field clears a nullable number
        if (!long.TryParse(text, NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign, culture, out var number))
            return DependencyProperty.UnsetValue; // shows the field as invalid and keeps the old value

        try
        {
            return System.Convert.ChangeType(number, type ?? targetType, culture);
        }
        catch (OverflowException)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
