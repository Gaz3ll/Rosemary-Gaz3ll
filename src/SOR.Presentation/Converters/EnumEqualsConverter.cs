using System.Globalization;
using System.Windows.Data;
using SOR.Application.DTOs;

namespace SOR.Presentation.Converters;

/// <summary>
/// Wiąże radio buttony filtru statusu z pojedynczą właściwością wyliczenia
/// (<see cref="StayFilter"/>). Radio button ustawia <c>IsChecked</c>, a konwerter
/// zamienia tę wartość na właściwość źródła; w drugą stronę porównuje wartość
/// źródła z parametrem konwertera, aby zaznaczyć właściwy przycisk.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Odwrócenie do zmiany właściwości źródła następuje wyłącznie przy zaznaczeniu
        // (IsChecked = true). Odznaczenie nie powinno nadpisywać wyboru użytkownika.
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
    }
}