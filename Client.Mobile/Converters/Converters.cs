using System.Globalization;

namespace CheckIn.Client.Mobile.Converters;

/// <summary>
/// Converts a boolean (IsCheckedIn) to a Color:
/// true -> Primary blue, false -> Gray (not checked in).
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isCheckedIn)
        {
            return isCheckedIn ? Color.FromArgb("#4285f4") : Color.FromArgb("#e8e8e8");
        }
        return Color.FromArgb("#e8e8e8");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a rank number to a display color:
/// 1 -> Gold, 2 -> Silver, 3 -> Bronze, others -> Dark gray.
/// </summary>
public class RankColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rank)
        {
            return rank switch
            {
                1 => Color.FromArgb("#FFD700"),
                2 => Color.FromArgb("#C0C0C0"),
                3 => Color.FromArgb("#CD7F32"),
                _ => Color.FromArgb("#333333")
            };
        }
        return Color.FromArgb("#333333");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverts a boolean value. Used for visibility toggles.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// Returns true if the string is not null or empty.
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 不排课日背景色：true -> 浅红，false -> 白色
/// </summary>
public class OffDayColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isOff && isOff ? Color.FromArgb("#fef2f2") : Colors.White;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 今日字体加粗：true -> Bold，false -> None
/// </summary>
public class BoldConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isToday && isToday ? FontAttributes.Bold : FontAttributes.None;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 今日文字颜色：true -> 主色蓝，false -> 深灰
/// </summary>
public class TodayColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isToday && isToday ? Color.FromArgb("#4285f4") : Color.FromArgb("#333333");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 不排课日文字：true -> "休"，false -> 空
/// </summary>
public class OffDayTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isOff && isOff ? "休" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 排课人数显示：0 -> 空，>0 -> "N人"
/// </summary>
public class ScheduleCountConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0 ? $"{count}人" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
