using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Application.Services;

/// <summary>
/// Serwis monitoringu obciążenia stref (BR-06, BR-07).
///
/// Klasa pełni wyłącznie rolę „obserwatora”: cyklicznie odpytuje <see cref="IZoneLoadQueryService"/>,
/// wykrywa zmiany statusu i publikuje zdarzenia dla warstwy prezentacji. Nie zawiera logiki
/// obliczeniowej, dzięki czemu graf zależności pozostaje acykliczny.
///
/// Dwa zdarzenia są świadomie oddzielone:
/// <list type="bullet">
///   <item><description><c>ZoneOverloaded</c> — przekroczenie progu, wymaga pilnej reakcji.</description></item>
///   <item><description><c>ZoneNormalized</c> — powrót do normy, komunikat informacyjny.</description></item>
/// </list>
/// </summary>
public sealed class ZoneLoadMonitoringService : IZoneLoadMonitoringService, IAsyncDisposable
{
    private readonly IZoneLoadQueryService _queryService;
    private readonly IAuditLogService _auditLog;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventPublisher _eventPublisher;

    private readonly Dictionary<Guid, ZoneLoadStatus> _lastKnownStatus = new();
    private string? _lastRecommendationSignature;
    private bool _disposed;

    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    public ZoneLoadMonitoringService(
        IZoneLoadQueryService queryService,
        IAuditLogService auditLog,
        IUnitOfWork unitOfWork,
        IDomainEventPublisher eventPublisher)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <inheritdoc />
    public event EventHandler<ZoneLoadDto>? ZoneOverloaded;

    /// <inheritdoc />
    public event EventHandler<ZoneLoadDto>? ZoneNormalized;

    /// <inheritdoc />
    public event EventHandler<RotationRecommendationDto>? RotationSuggested;

    /// <inheritdoc />
    public async Task<DepartmentLoadSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        await PublishTransitionsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await PublishRecommendationAsync(cancellationToken).ConfigureAwait(false);

        return snapshot;
    }

    /// <inheritdoc />
    public Task<ZoneLoadDto> GetZoneLoadAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        _queryService.GetZoneLoadAsync(zoneId, cancellationToken);

    /// <inheritdoc />
    public Task StartMonitoringAsync(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, "Interwał monitoringu musi być dodatni.");

        StopMonitoring();

        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _monitoringCts.Token;

        _monitoringTask = Task.Run(
            async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await GetSnapshotAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
#pragma warning disable CA1031 // pojedynczy nieudany cykl nie może zatrzymać pętli monitoringu
                    catch (Exception)
                    {
                        // Błąd pojedynczego cyklu jest ignorowany — pętla wykonuje kolejny pomiar.
                    }
#pragma warning restore CA1031

                    try
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            },
            CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void StopMonitoring()
    {
        if (_monitoringCts is not null)
        {
            _monitoringCts.Cancel();
            _monitoringCts.Dispose();
            _monitoringCts = null;
        }

        _monitoringTask = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitoring();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ---------- Publikacja zdarzeń ----------

    /// <summary>
    /// Wykrywa przejścia statusu i publikuje zdarzenia. Zdarzenie alarmowe jest rejestrowane
    /// w dzienniku audytu oraz w strumieniu zdarzeń domenowych (dla integracji z systemem zewnętrznym).
    /// Powrót do normy jest komunikatem informacyjnym i nie trafia do audytu operacyjnego.
    /// </summary>
    private async Task PublishTransitionsAsync(
        DepartmentLoadSnapshotDto snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var zone in snapshot.Zones)
        {
            if (!_lastKnownStatus.TryGetValue(zone.ZoneId, out var previous))
            {
                // Pierwsze odczytanie stanu nie jest zmianą — nie wywołujemy fałszywego alarmu.
                _lastKnownStatus[zone.ZoneId] = zone.Status;
                continue;
            }

            if (previous < ZoneLoadStatus.Warning && zone.Status >= ZoneLoadStatus.Warning)
            {
                await RecordOverloadAsync(zone, cancellationToken).ConfigureAwait(false);

                _eventPublisher.PublishZoneOverloaded(zone);
                ZoneOverloaded?.Invoke(this, zone);
            }
            else if (previous >= ZoneLoadStatus.Warning && zone.Status < ZoneLoadStatus.Warning)
            {
                _eventPublisher.PublishZoneNormalized(zone);
                ZoneNormalized?.Invoke(this, zone);
            }

            _lastKnownStatus[zone.ZoneId] = zone.Status;
        }
    }

    /// <summary>
    /// Publikuje rekomendację rotacji wyłącznie wtedy, gdy zmieniła się w stosunku do poprzedniej.
    /// Zapobiega to powstawaniu identycznych wpisów audytowych przy każdym cyklu monitoringu.
    /// </summary>
    private async Task PublishRecommendationAsync(CancellationToken cancellationToken)
    {
        var recommendation = await _queryService.GetRecommendationAsync(cancellationToken).ConfigureAwait(false);

        var signature = recommendation is null
            ? null
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{recommendation.CandidateUserId:N}:{recommendation.TargetZoneId:N}");

        if (signature == _lastRecommendationSignature)
        {
            return;
        }

        _lastRecommendationSignature = signature;

        if (recommendation is null)
        {
            return;
        }

        await _auditLog.RecordAsync(
            AuditActionType.RotationRecommendationGenerated,
            null,
            "SYSTEM",
            recommendation.CandidateUserId,
            nameof(RotationRecommendationDto),
            recommendation.BannerText,
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        RotationSuggested?.Invoke(this, recommendation);
    }

    /// <summary>Zapisuje w dzienniku audytu wykrycie przekroczenia progu obciążenia.</summary>
    private async Task RecordOverloadAsync(ZoneLoadDto zone, CancellationToken cancellationToken)
    {
        await _auditLog.RecordAsync(
            AuditActionType.ZoneOverloadDetected,
            null,
            "SYSTEM",
            zone.ZoneId,
            nameof(Zone),
            $"Strefa '{zone.ZoneName}' przekroczyła próg obciążenia ({zone.LoadRatio:0.00}). {zone.StatusDescription}",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}