using System.Windows;
using System.Windows.Controls;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation;

/// <summary>
/// Widok główny aplikacji. Kod-behind ogranicza się do obsługi zdarzeń, których nie da się
/// wyrazić wiązaniem danych — w tym przypadku <see cref="PasswordBox"/>, który celowo nie
/// udostępnia właściwości zależności dla hasła, oraz odpowiedzi ViewModelu sesji.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is MainViewModel viewModel)
        {
            viewModel.Session.Password = box.Password;
        }
    }

    /// <summary>
    /// Po zalogowaniu ładuje formularz leków, pakiety i katalog rozpoznań. Dane referencyjne
    /// nie są pobierane przed uzyskaniem sesji, aby nie zapełniać list cudzymi danymi.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel previous)
        {
            previous.Session.SessionStateChanged -= OnSessionStateChanged;
        }

        if (e.NewValue is MainViewModel current)
        {
            current.Session.SessionStateChanged += OnSessionStateChanged;
        }
    }

    private async void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Session.IsAuthenticated)
        {
            await viewModel.Catalog.InitializeAsync();
        }
        else
        {
            // Po wylogowaniu formularz nie może pokazywać danych poprzedniego użytkownika.
            viewModel.Catalog.Clear();
        }
    }
}
