using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;

namespace GasGuard.Application.Abstractions;

public interface IDeviceRepository
{
    Task<GasDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GasDevice?> GetByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);
    Task<GasDevice?> GetBySenderAsync(string normalizedSender, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GasDevice>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludingDeviceId, CancellationToken cancellationToken = default);
    Task<bool> SenderExistsAsync(string normalizedSender, Guid? excludingDeviceId, CancellationToken cancellationToken = default);
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);
    Task AddAsync(GasDevice device, CancellationToken cancellationToken = default);
    void Update(GasDevice device);
}

public interface IIncidentRepository
{
    Task AddAsync(Incident incident, CancellationToken cancellationToken = default);
    Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Incident?> GetByDedupKeyAsync(string dedupKey, CancellationToken cancellationToken = default);
    Task<Incident?> GetRecentForDedupAsync(
        Guid deviceId,
        Severity severity,
        long? sequenceNumber,
        string fingerprint,
        bool isSimulated,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default);
    Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Incident>> GetActiveAsync(CancellationToken cancellationToken = default);
}

public interface ISmsMessageRepository
{
    Task AddAsync(SmsMessage message, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> FingerprintExistsAsync(string fingerprint, CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetLatestProcessedByFingerprintSinceAsync(
        string fingerprint,
        bool isSimulated,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default);
    Task<SmsMessage?> GetLatestBySourceReferenceAsync(string sourceReference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> GetRecentAsync(int take, CancellationToken cancellationToken = default);
}

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Contact>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(string normalizedPhone, Guid? excludingContactId, CancellationToken cancellationToken = default);
    Task AddAsync(Contact contact, CancellationToken cancellationToken = default);
    void Update(Contact contact);
}

public interface ISettingsRepository
{
    Task<ModemConfiguration> GetModemConfigurationAsync(CancellationToken cancellationToken = default);
    Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);
}

public interface ICommunicationLogRepository
{
    Task AddAsync(CommunicationLog log, CancellationToken cancellationToken = default);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken cancellationToken = default);
}
