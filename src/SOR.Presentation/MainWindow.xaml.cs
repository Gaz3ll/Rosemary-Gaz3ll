using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SOR.Application.DTOs;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation;

/// <summary>
/// Widok główny aplikacji. Kod-behind ogranicza się do obsługi zdarzeń, których nie da się
/// wyrazić wiązaniem danych — w tym przypadku <see cref="PasswordBox"/>, który celowo nie
/// udostępnia właściwości zależności dla hasła, odpowiedzi ViewModelu sesji oraz dwukrotnego
/// kliknięcia wiersza pacjenta (DataGrid nie udostępnia wiązania wyzwalanego dwuklikiem).
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
    /// Dwukrotne kliknięcie wiersza pacjenta otwiera okno „Historia pobytu”. Polecenie musi
    /// otrzymać wiersz spod kursora, dlatego korzystamy z <see cref="DataGrid.SelectedItem"/>;
    /// kliknięcie nagłówka lub paska przewijania jest ignorowane.
    /// </summary>
    private void PatientRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (FindRow(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        var row = grid.SelectedItem;

        ICommand? command = row switch
        {
            PatientStayDto => viewModel.Stays.OpenPatientHistoryCommand,
            PatientCardDto => viewModel.Board.OpenPatientHistoryCommand,
            _ => null,
        };

        if (command?.CanExecute(row) == true)
        {
            command.Execute(row);
        }
    }

    private static DataGridRow? FindRow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DataGridRow row)
            {
                return row;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    /// <summary>
    /// Po zalogowaniu ładuje formularz leków. Dane referencyjne nie są pobierane przed
    /// uzyskaniem sesji, aby nie zapełniać list cudzymi danymi.
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
