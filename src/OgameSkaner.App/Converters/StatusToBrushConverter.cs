using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Converters;

/// <summary>Maps <see cref="OperationStatus"/> and <see cref="SessionStatus"/> to an indicator colour.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        OperationStatus.Succeeded or SessionStatus.Valid => Brushes.LimeGreen,
        OperationStatus.Failed or SessionStatus.Invalid => Brushes.Red,
        OperationStatus.Pending or SessionStatus.Checking => Brushes.Orange,
        SessionStatus.Unknown => Brushes.Gray,
        _ => Brushes.Transparent
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
