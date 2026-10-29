using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;

namespace GasGuard.Application.Abstractions;

public interface ISmsIngestionService
{
    Task<IngestionResult> ProcessAsync(IncomingSms incomingSms, CancellationToken cancellationToken = default);
}

public interface IIncomingSmsJournal
{
    Task<SmsJournalEntry> PersistAsync(IncomingSms message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsJournalEntry>> LoadPendingAsync(CancellationToken cancellationToken = default);
    Task RemoveAsync(string journalId, CancellationToken cancellationToken = default);
}

public sealed record SmsJournalEntry(string JournalId, IncomingSms Message);

public interface IIncidentService
{
    Task<PagedResult<IncidentListItem>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken = default);
    Task<IncidentDetails?> GetDetailsAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncidentListItem>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> AcknowledgeAsync(Guid incidentId, string? notes, CancellationToken cancellationToken = default);
    Task<OperationResult> ResolveAsync(Guid incidentId, string notes, CancellationToken cancellationToken = default);
}

public interface IDeviceService
{
    Task<IReadOnlyList<DeviceListItem>> GetDevicesAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<DeviceListItem?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(DeviceInput input, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(Guid deviceId, DeviceInput input, CancellationToken cancellationToken = default);
    Task<OperationResult> SetActiveAsync(Guid deviceId, bool active, CancellationToken cancellationToken = default);
}

public interface IContactService
{
    Task<IReadOnlyList<ContactListItem>> GetContactsAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(ContactInput input, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(Guid contactId, ContactInput input, CancellationToken cancellationToken = default);
    Task<OperationResult> SetActiveAsync(Guid contactId, bool active, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<SettingsOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<SettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveModemSettingsAsync(ModemSettingsInput input, CancellationToken cancellationToken = default);
    Task<OperationResult> SavePreferencesAsync(PreferencesInput input, CancellationToken cancellationToken = default);
    Task<TimeSpan> GetStaleAfterAsync(CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<ReportSummary> GetSummaryAsync(ReportCriteria criteria, CancellationToken cancellationToken = default);
    Task<OperationResult> ExportIncidentsCsvAsync(ReportCriteria criteria, string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> ExportCommunicationFailuresCsvAsync(ReportCriteria criteria, string filePath, CancellationToken cancellationToken = default);
    Task<OperationResult> ExportDetectorActivityCsvAsync(ReportCriteria criteria, string filePath, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<DatabaseHealthInfo> GetDatabaseHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<BackupValidationResult> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default);
    Task<BackupCreationResult> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreBackupAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IAuditLogService
{
    Task RecordAsync(AuditEntryDraft entry, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditEntryItem>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntryItem>> GetForEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
}

public interface IRetentionService
{
    Task<RetentionResult> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}

public interface ISmsEvidenceService
{
    Task<IReadOnlyList<SmsEvidenceItem>> GetRecentAsync(int take = 50, CancellationToken cancellationToken = default);
}

public interface ICommunicationLogService
{
    Task<PagedResult<CommunicationLogItem>> QueryAsync(CommunicationLogQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommunicationLogItem>> GetOutgoingAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommunicationLogItem>> GetRecentFailuresAsync(int take, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<SmsSendResult> SendTestMessageAsync(
        string recipientNumber,
        string message,
        bool operatorConfirmed,
        CancellationToken cancellationToken = default);

    Task<NotificationDispatchResult> DispatchIncidentNotificationsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);

    Task<NotificationDispatchResult> DispatchCommunicationFailureNotificationAsync(
        string failureSummary,
        CancellationToken cancellationToken = default);
}

public interface IDemoScenarioService
{
    Task<bool> IsDemoDataLoadedAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> LoadDemoDataAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> InjectDemoMessageAsync(
        string deviceCode,
        GasEventCode eventCode,
        int reading,
        CancellationToken cancellationToken = default);
}

public interface ISmsProcessingCoordinator
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task WaitForIngressDrainAsync(CancellationToken cancellationToken = default);
    Task<int> ReconcileNowAsync(CancellationToken cancellationToken = default);
}

public interface IAppEventBus
{
    event Action<MonitorNotification>? NotificationRaised;
    event Action<Guid>? IncidentCreated;
    event Action? IncidentStateChanged;
    void PublishNotification(MonitorNotification notification);
    void PublishIncidentCreated(Guid incidentId);
    void PublishIncidentStateChanged();
}

public interface IStartupService
{
    Task<StartupResult> InitializeAsync(CancellationToken cancellationToken = default);
    Task<StartupResult> ApplyMigrationsAsync(CancellationToken cancellationToken = default);
}

public interface IAppPathProvider
{
    string RootDirectory { get; }
    string DatabasePath { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
}

public sealed record StartupResult(
    bool Success,
    string Message,
    string DatabasePath,
    bool Migrated,
    string? PreMigrationBackupPath = null,
    Exception? Exception = null);
