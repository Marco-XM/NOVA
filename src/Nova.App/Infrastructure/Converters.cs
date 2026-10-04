using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nova.App.Infrastructure;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Converts a 0..1 fraction to a percentage string.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d * 100)}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a double with the format given as parameter (e.g. "0.00×").</summary>
public sealed class NumberFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d.ToString(parameter as string ?? "0.##", culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when all bound values are equal (used to highlight the selected tile).</summary>
public sealed class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s)) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}