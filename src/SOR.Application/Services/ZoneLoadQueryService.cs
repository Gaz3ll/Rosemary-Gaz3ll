using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Application.Mapping;
using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Services;

/// <summary>
/// Bezpieczny dla wątków odczyt stanu obciążenia stref i wyliczenie rekomendacji rotacji.
///
/// Klasa jest celowo pozbawiona efektów ubocznych: nie publikuje zdarzeń, nie zapisuje wpisów
/// audytowych i nie uruchamia pętli monitoringu. Dzięki temu może być wstrzykiwana zarówno do
/// serwisu monitoringu, jak i do serwisu rotacji, nie tworząc cyklu zależności.
/// </summary>
public sealed class ZoneLoadQueryService : IZoneLoadQueryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IZoneLoadCalculator _calculator;
    private readonly IRotationRecommendationEngine _recommendationEngine;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _concurrencyGuard = new(1, 1);

    public ZoneLoadQueryService(
        IUnitOfWork unitOfWork,
        IZoneLoadCalculator calculator,
        IRotationRecommendationEngine recommendationEngine,
        IClock clock)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<DepartmentLoadSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // Serwis może być odpytywany równolegle przez pętlę monitoringu i przez akcję użytkownika.
        await _concurrencyGuard.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var zones = await _unitOfWork.Zones.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            var patients = await _unitOfWork.Patients.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var assignments = await _unitOfWork.StaffAssignments.GetAllActiveAsync(cancellationToken).ConfigureAwait(false);

            var now = _clock.UtcNow;
            var zoneDtos = new List<ZoneLoadDto>(zones.Count);
            var zoneSnapshots = new List<ZoneLoadSnapshot>(zones.Count);

            foreach (var zone in zones)
            {
                var zonePatients = patients
                    .Where(p => p.ZoneId == zone.Id)
                    .Select(p => new PatientLoadSnapshot(
                        p.Id,
                        p.FullName,
                        p.CurrentTriage?.Category ?? TriageCategory.Green,
                        p.State,
                        p.ZoneAssignedAtUtc is null ? TimeSpan.Zero : now - p.ZoneAssignedAtUtc.Value))
                    .ToList();

                var staffCount = assignments.Count(a => a.ZoneId == zone.Id && a.Kind != ReassignmentKind.Release);

                var hasPendingRed = zonePatients.Any(p =>
                    p.TriageCategory == TriageCategory.Red &&
                    p.IsActive &&
                    p.State == PatientState.Triaged);

                var context = ZoneLoadContext.Create(
                    zone.Id, zone.Name, zone.Kind, zonePatients, staffCount, hasPendingRed);

                var loadRatio = _calculator.Calculate(context);

                zoneSnapshots.Add(new ZoneLoadSnapshot(zone.Id, zone.Name, zone.Kind, loadRatio));
                zoneDtos.Add(zone.ToDto(loadRatio, isCurrentUserZone: false));
            }

            return new DepartmentLoadSnapshotDto(zoneDtos, now);
        }
        finally
        {
            _concurrencyGuard.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ZoneLoadDto> GetZoneLoadAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshot.Zones.FirstOrDefault(z => z.ZoneId == zoneId)
               ?? throw new EntityNotFoundException(nameof(Zone), zoneId);
    }

    /// <inheritdoc />
    public async Task<RotationRecommendationDto?> GetRecommendationAsync(CancellationToken cancellationToken = default)
    {
        await _concurrencyGuard.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var assignments = await _unitOfWork.StaffAssignments.GetAllActiveAsync(cancellationToken).ConfigureAwait(false);
            var patients = await _unitOfWork.Patients.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var zones = await _unitOfWork.Zones.GetActiveAsync(cancellationToken).ConfigureAwait(false);

            var zoneSnapshots = new List<ZoneLoadSnapshot>(zones.Count);
            var staffPool = await BuildStaffPoolAsync(assignments, patients, cancellationToken).ConfigureAwait(false);

            foreach (var zone in zones)
            {
                var zonePatientList = patients
                    .Where(p => p.ZoneId == zone.Id)
                    .Select(p => new PatientLoadSnapshot(
                        p.Id,
                        p.FullName,
                        p.CurrentTriage?.Category ?? TriageCategory.Green,
                        p.State,
                        p.ZoneAssignedAtUtc is null ? TimeSpan.Zero : _clock.UtcNow - p.ZoneAssignedAtUtc.Value))
                    .ToList();

                var staffCount = assignments.Count(a => a.ZoneId == zone.Id && a.Kind != ReassignmentKind.Release);

                var context = ZoneLoadContext.Create(
                    zone.Id,
                    zone.Name,
                    zone.Kind,
                    zonePatientList,
                    staffCount,
                    zonePatientList.Any(p =>
                        p.TriageCategory == TriageCategory.Red && p.IsActive && p.State == PatientState.Triaged));

                zoneSnapshots.Add(new ZoneLoadSnapshot(zone.Id, zone.Name, zone.Kind, _calculator.Calculate(context)));
            }

            var recommendation = _recommendationEngine.Recommend(new RotationEvaluationContext(staffPool, zoneSnapshots));

            if (recommendation is null)
            {
                return null;
            }

            return new RotationRecommendationDto(
                recommendation.CandidateUserId,
                recommendation.CandidateDisplayName,
                recommendation.SourceZoneId,
                recommendation.SourceZoneName,
                recommendation.TargetZoneId,
                recommendation.TargetZoneName,
                recommendation.TargetLoadRatio,
                recommendation.SourceLoadRatio,
                recommendation.ReliefGain,
                recommendation.Describe(),
                _clock.UtcNow);
        }
        finally
        {
            _concurrencyGuard.Release();
        }
    }

    /// <summary>Buduje listę personelu z indywidualnym obciążeniem pacjentami.</summary>
    private async Task<List<StaffLoadInfo>> BuildStaffPoolAsync(
        IReadOnlyList<StaffZoneAssignment> assignments,
        IReadOnlyList<Patient> patients,
        CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
        {
            return new List<StaffLoadInfo>();
        }

        var users = await _unitOfWork.Users
            .GetByIdsAsync(assignments.Select(a => a.UserId).Distinct().ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var zones = await _unitOfWork.Zones
            .GetByIdsAsync(assignments.Select(a => a.ZoneId).Distinct().ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var userById = users.ToDictionary(u => u.Id);
        var zoneById = zones.ToDictionary(z => z.Id);

        var patientByZone = patients
            .Where(p => p.ZoneId.HasValue && p.State != PatientState.Closed)
            .GroupBy(p => p.ZoneId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var pool = new List<StaffLoadInfo>(assignments.Count);

        foreach (var assignment in assignments)
        {
            if (assignment.Kind == ReassignmentKind.Release ||
                !userById.TryGetValue(assignment.UserId, out var user) ||
                !zoneById.TryGetValue(assignment.ZoneId, out var zone))
            {
                continue;
            }

            pool.Add(new StaffLoadInfo(
                user.Id,
                user.DisplayName,
                zone.Id,
                zone.Name,
                user.Role,
                patientByZone.GetValueOrDefault(zone.Id, 0)));
        }

        return pool;
    }
}