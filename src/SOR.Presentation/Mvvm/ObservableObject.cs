using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SOR.Presentation.Mvvm;

/// <summary>
/// Minimalna implementacja interfejsu INotifyPropertyChanged wymagana przez wzorzec MVVM.
/// Zawiera potok <c>CallerMemberName</c>, dzięki czemu wywołania w setterach nie wymagają
/// przekazywania nazwy właściwości ręcznie.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wywołuje zdarzenie zmiany właściwości.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Przypisuje wartość do pola i zgłasza zmianę wyłącznie wtedy, gdy wartość faktycznie się różni.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}