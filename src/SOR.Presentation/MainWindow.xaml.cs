using System.Windows;
using System.Windows.Controls;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation;

/// <summary>
/// Widok główny aplikacji. Kod-behind ogranicza się do obsługi zdarzeń, których nie da się
/// wyrazić wiązaniem danych — w tym przypadku <see cref="PasswordBox"/>, który celowo nie
/// udostępnia właściwości zależności dla hasła.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is MainViewModel viewModel)
        {
            viewModel.Session.Password = box.Password;
        }
    }
}
