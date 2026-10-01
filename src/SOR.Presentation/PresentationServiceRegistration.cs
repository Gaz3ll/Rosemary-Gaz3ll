using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SOR.Presentation.Services;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation;

/// <summary>
/// Rejestracja warstwy prezentacji w kontenerze zależności.
///
/// ViewModele są rejestrowane jako <c>Scoped</c>, ponieważ korzystają z usług aplikacyjnych
/// o tym samym cyklu życia (współdzielony kontekst EF Core i sesja użytkownika). Aplikacja
/// desktopowa tworzy jeden zakres na cały czas działania, więc w praktyce instancje są trwałe.
/// </summary>
public static class PresentationServiceRegistration
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ => new UiThreadDispatcher(
            System.Windows.Application.Current?.Dispatcher
            ?? System.Windows.Threading.Dispatcher.CurrentDispatcher));

        services.AddScoped<SessionViewModel>();
        services.AddScoped<PatientBoardViewModel>();
        services.AddScoped<MainViewModel>();

        return services;
    }
}
