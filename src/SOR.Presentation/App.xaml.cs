using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Infrastructure;
using SOR.Presentation.Services;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation;

/// <summary>
/// Composition Root aplikacji desktopowej.
///
/// Aplikacja WPF nie ma naturalnego zakresu żądania, dlatego tworzymy pojedynczy zakres
/// usług na cały czas życia procesu. Dzięki temu sesja użytkownika i kontekst EF Core są
/// współdzielone przez wszystkie ViewModele i usługi aplikacyjne — zgodnie z zasadą
/// „logowania kontekstowego" (jedna strefa robocza obowiązuje przez całą sesję).
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private AsyncServiceScope _scope;
    private bool _scopeCreated;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportFatal(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportFatal(args.Exception);
            args.SetObserved();
        };

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSorSystem(ResolveDatabasePath());
        services.AddPresentation();

        _serviceProvider = services.BuildServiceProvider();

        try
        {
            await _serviceProvider.InitializeDatabaseAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Nie udało się zainicjalizować bazy danych.\n\n{exception.Message}",
                "Błąd startu systemu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _scope = _serviceProvider.CreateAsyncScope();
        _scopeCreated = true;

        // Motyw jest stosowany przed utworzeniem okna, aby widok startował już w właściwych kolorach.
        _scope.ServiceProvider.GetRequiredService<IThemeService>().Initialize();

        var mainViewModel = _scope.ServiceProvider.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_scopeCreated)
        {
            _scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatal(e.Exception);
        e.Handled = true;
    }

    private static void ReportFatal(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        MessageBox.Show(
            $"Wystąpił nieoczekiwany błąd aplikacji:\n\n{exception.Message}",
            "Błąd krytyczny",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Baza danych trafia do profilu użytkownika, aby aplikacja działała bez uprawnień
    /// administracyjnych i nie zaśmiecała katalogu instalacyjnego.
    /// </summary>
    private static string ResolveDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SOR");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "sor.db");
    }
}
