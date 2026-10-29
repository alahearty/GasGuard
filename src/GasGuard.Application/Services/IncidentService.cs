using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Mapping;
using GasGuard.Application.Models;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class IncidentService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IClock clock,
    IUserContext userContext,
    IAuditLogService auditLog,
    IAppEventBus eventBus,
    ILogger<IncidentService> logger) : IIncidentService
{
    private const int MaximumNoteLength = 2000;

    private void PublishStateChangedSafe()
    {
        try
        {
            eventBus.PublishIncidentStateChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An incident-state event handler failed.");
        }
    }

    public async Task<PagedResult<IncidentListItem>> QueryAsync(
        IncidentQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var page = await unitOfWork.Incidents.QueryAsync(query, cancellationToken);
        return new PagedResult<IncidentListItem>(
            page.Items.Select(IncidentMapping.ToListItem).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<IncidentDetails?> GetDetailsAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var incident = await unitOfWork.Incidents.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        var history = await auditLog.GetForEntityAsync("Incident", incident.Id.ToString(), cancellationToken);
        var sms = incident.TriggeringSmsMessage;

        return new IncidentDetails(
            IncidentMapping.ToListItem(incident),
            sms.Id,
            sms.BodyRaw,
            sms.SenderNumberRaw,
            sms.SenderNumberNormalized,
            sms.ProtocolVersion,
            sms.ProcessingStatus,
            incident.AcknowledgedBy,
            incident.AcknowledgementNotes,
            incident.ResolvedBy,
            incident.ResolutionNotes,
            history);
    }

    public async Task<IReadOnlyList<IncidentListItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var incidents = await unitOfWork.Incidents.GetActiveAsync(cancellationToken);
        return incidents.Select(IncidentMapping.ToListItem).ToArray();
    }

    public async Task<OperationResult> AcknowledgeAsync(
        Guid incidentId,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (notes?.Length > MaximumNoteLength)
        {
            return OperationResult.Fail(
                "The acknowledgement note is too long.",
                [$"Acknowledgement notes cannot exceed {MaximumNoteLength} characters."]);
        }

        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var incident = await unitOfWork.Incidents.GetByIdAsync(incidentId, cancellationToken);
            if (incident is null)
            {
                return OperationResult.Fail("The incident no longer exists.");
            }

            var now = clock.UtcNow;
            incident.Acknowledge(userContext.OperatorName, notes, now);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "IncidentAcknowledged",
                "Incident",
                incident.Id.ToString(),
                AuditOutcome.Success,
                $"Incident {incident.DeviceCodeSnapshot} acknowledged. Acknowledgement does not mean the hazard has cleared."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            PublishStateChangedSafe();

            return OperationResult.Ok(
                "Incident acknowledged. Acknowledgement records operator awareness only; it does not resolve the hazard.");
        }
        catch (DomainException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acknowledge incident {IncidentId}.", incidentId);
            return OperationResult.Fail("The acknowledgement could not be saved. The incident history was not changed.");
        }
    }

    public async Task<OperationResult> ResolveAsync(
        Guid incidentId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(notes))
        {
            errors.Add("Resolution notes are required.");
        }
        else if (notes.Length > MaximumNoteLength)
        {
            errors.Add($"Resolution notes cannot exceed {MaximumNoteLength} characters.");
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail("The incident was not resolved.", errors);
        }

        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var incident = await unitOfWork.Incidents.GetByIdAsync(incidentId, cancellationToken);
            if (incident is null)
            {
                return OperationResult.Fail("The incident no longer exists.");
            }

            var now = clock.UtcNow;
            incident.Resolve(userContext.OperatorName, notes, now);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "IncidentResolved",
                "Incident",
                incident.Id.ToString(),
                AuditOutcome.Success,
                $"Incident {incident.DeviceCodeSnapshot} resolved by an operator. Resolution was recorded explicitly."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            PublishStateChangedSafe();

            return OperationResult.Ok("Incident resolved and the resolution record was saved.");
        }
        catch (DomainException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve incident {IncidentId}.", incidentId);
            return OperationResult.Fail("The resolution could not be saved. The incident remains unresolved.");
        }
    }
}
