using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Application.Interfaces;
using SOR.Application.Services;
using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.ValueObjects;
using SOR.Infrastructure.Persistence;
using SOR.Infrastructure.Resilience;

namespace SOR.Infrastructure;

/// <summary>
/// Composition Root — jedyne miejsce w programie, w którym znane są konkretne implementacje
/// wszystkich abstrakcji. Pozostałe warstwy operują wyłącznie na interfejsach.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>Rejestruje warstwę trwałości i serwisy aplikacyjne.</summary>
    /// <param name="services">Kontener zależności.</param>
    /// <param name="databasePath">Ścieżka pliku bazy SQLite.</param>
    /// <param name="thresholds">Progi obciążenia stref (parametryzowalne z UI lub pliku konfiguracyjnego).</param>
    public static IServiceCollection AddSorSystem(
        this IServiceCollection services,
        string databasePath,
        ZoneLoadThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var effectiveThresholds = thresholds ?? ZoneLoadThresholds.Default;

        // ---------- Warstwa domeny ----------
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton(effectiveThresholds);
        services.AddSingleton<IZoneLoadCalculator, ZoneLoadCalculator>();
        services.AddSingleton<IRotationRecommendationEngine, RotationRecommendationEngine>();
        services.AddSingleton<ZoneLoadCalculator>();

        // ---------- Warstwa trwałości ----------
        services.AddSingleton<IRetryPolicy, SqliteRetryPolicy>();
        services.AddDbContext<SorDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath};Cache=Shared;Foreign Keys=True");
            options.EnableDetailedErrors();
        });

        services.AddScoped<IUnitOfWork, SqliteUnitOfWork>();

        // Ścieżka pliku bady jest dostępna dla inicjalizatora i komunikatów diagnostycznych.
        services.AddSingleton(new DatabaseConnectionInfo(databasePath));

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<DatabaseSeeder>();

        // ---------- Warstwa aplikacji ----------

        // Graf zależności jest acykliczny dzięki wydzieleniu odczytu obciążenia stref
        // (IZoneLoadQueryService) od monitoringu publikującego zdarzenia:
        //
        //   IZoneLoadQueryService ──> IZoneLoadCalculator, IRotationRecommendationEngine
        //   IZoneLoadMonitoringService ──> IZoneLoadQueryService, IAuditLogService, IDomainEventPublisher
        //   IStaffRotationService ──> IZoneLoadQueryService, IAuthenticationService
        //
        // Serwis monitoringu NIE zależy od serwisu rotacji, więc obie usługi można
        // zarejestrować zwykłymi wywołaniami AddScoped<TService, TImplementation>().
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IZoneLoadQueryService, ZoneLoadQueryService>();
        services.AddScoped<IStaffRotationService, StaffRotationService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IZoneLoadMonitoringService, ZoneLoadMonitoringService>();

        return services;
    }

    /// <summary>
    /// Wykonuje inicjalizację schematu i danych startowych.
    /// Wywoływane przy starcie aplikacji — obsługuje blokady pliku bazy.
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
    }
}