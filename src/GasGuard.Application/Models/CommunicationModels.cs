using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record CommunicationLogItem(
    Guid Id,
    DateTime TimestampUtc,
    CommunicationDirection Direction,
    CommunicationKind Kind,
    CommunicationOutcome Outcome,
    string Summary,
    string? Detail,
    string? ErrorCode,
    string? RecipientNumber,
    string? ProviderReference,
    bool IsSimulated);

public sealed record CommunicationLogQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    CommunicationDirection? Direction = null,
    CommunicationOutcome? Outcome = null,
    CommunicationKind? Kind = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);
