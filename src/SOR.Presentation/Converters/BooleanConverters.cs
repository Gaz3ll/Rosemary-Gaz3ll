using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SOR.Presentation.Converters;

/// <summary>
/// Zamienia wartość logiczną na widoczność elementu (true → Visible).
/// Dzięki temu widoki pozostają deklaratywne i nie zawierają kodu sterującego.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Odwrotność <see cref="BooleanToVisibilityConverter"/> (true → Collapsed).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Zamienia niepusty tekst na widoczność — używane do pasków statusu i komunikatów błędu,
/// które nie powinny zajmować miejsca, gdy są puste.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
