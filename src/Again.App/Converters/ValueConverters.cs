using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Again.App.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool boolean => boolean,
            int count => count > 0,
            long countLong => countLong > 0,
            string str => !string.IsNullOrEmpty(str),
            _ => value != null
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value?.ToString() ?? string.Empty;
        return status switch
        {
            "Success" => new SolidColorBrush(Color.FromRgb(16, 185, 129)), // Emerald
            "LaunchedAndPlaced" => new SolidColorBrush(Color.FromRgb(16, 185, 129)), // Emerald
            "RepositionedOnly" => new SolidColorBrush(Color.FromRgb(20, 184, 166)), // Cyan/Teal
            "Partial" => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Amber
            "AlreadyRunning" => new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate
            "Skipped" => new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            "Failed" => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
            "Cancelled" => new SolidColorBrush(Color.FromRgb(249, 115, 22)), // Orange
            _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PinGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool pinned = value is true;
        return pinned ? "\uE718" : "\uE840";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
