using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record AuditEntryDraft(
    string Actor,
    string Action,
    string EntityType,
    string? EntityId,
    AuditOutcome Outcome,
    string Details);

public sealed record AuditEntryItem(
    Guid Id,
    DateTime TimestampUtc,
    string Actor,
    string Action,
    string EntityType,
    string? EntityId,
    AuditOutcome Outcome,
    string Details);

public sealed record AuditQuery(
    string? Search = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Action = null,
    AuditOutcome? Outcome = null,
    int Page = 1,
    int PageSize = 50);
