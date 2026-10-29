using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain.Entities;

namespace GasGuard.Application.Auditing;

public static class AuditLogExtensions
{
    public static Task AddAuditLogAsync(
        this IGasGuardUnitOfWork unitOfWork,
        AuditEntryDraft entry,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default) =>
        unitOfWork.AuditLogs.AddAsync(AuditLogFactory.Create(entry, timestampUtc), cancellationToken);
}
