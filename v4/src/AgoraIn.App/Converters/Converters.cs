using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AgoraIn.App.Converters;

/// <summary>布尔值 → 前景色：true=黑色，false=浅灰。</summary>
public sealed class BoolToForegroundConverter : IValueConverter
{
    public static readonly BoolToForegroundConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Black : new SolidColorBrush(Color.Parse("#CCCCCC"));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>双值 → 颜色：正值绿，负值红。</summary>
public sealed class HoursToColorConverter : IValueConverter
{
    public static readonly HoursToColorConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && d < 0
            ? new SolidColorBrush(Color.Parse("#ea4335"))
            : new SolidColorBrush(Color.Parse("#34a853"));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>座位背景：空位浅灰，已占用白底。</summary>
public sealed class SeatBgConverter : IValueConverter
{
    public static readonly SeatBgConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#f0f0f0"))
            : new SolidColorBrush(Colors.White);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
