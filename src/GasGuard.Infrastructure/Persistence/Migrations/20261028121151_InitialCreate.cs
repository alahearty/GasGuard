using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GasGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StaleAfterMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DataRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AutomaticContactNotifications = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotificationMaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumLogLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastBackupAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastBackupPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnCritical = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnWarning = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnCommunicationFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GasDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    InstallationLocation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExpectedSenderNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DetectorType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    InstallationDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSuccessfulCommunicationUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReportedState = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReading = table.Column<int>(type: "INTEGER", nullable: true),
                    LastReadingUnit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    LastEventCode = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCommunicationErrorUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCommunicationError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MaintenanceNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModemConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UseSimulatedModem = table.Column<bool>(type: "INTEGER", nullable: false),
                    PortName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaudRate = table.Column<int>(type: "INTEGER", nullable: false),
                    DataBits = table.Column<int>(type: "INTEGER", nullable: false),
                    Parity = table.Column<int>(type: "INTEGER", nullable: false),
                    StopBits = table.Column<int>(type: "INTEGER", nullable: false),
                    Handshake = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    PollIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoConnect = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoReconnect = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeleteSmsAfterPersistence = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModemConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommunicationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RecipientNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ProviderReference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsSimulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModemStorageIndex = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunicationLogs_GasDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "GasDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SmsMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderNumberRaw = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SenderNumberNormalized = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BodyRaw = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModemStorageIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    IsSimulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessingDetail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ProtocolVersion = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    EventCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Reading = table.Column<int>(type: "INTEGER", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    DeviceTimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeviceTimestampTrusted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsMessages_GasDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "GasDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggeringSmsMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DeviceNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LocationSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OccurredTimeSource = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reading = table.Column<int>(type: "INTEGER", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: true),
                    DedupKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    IsSimulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AcknowledgementNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidents_GasDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "GasDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incidents_SmsMessages_TriggeringSmsMessageId",
                        column: x => x.TriggeringSmsMessageId,
                        principalTable: "SmsMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AppPreferences",
                columns: new[] { "Id", "AutomaticContactNotifications", "DataRetentionDays", "LastBackupAtUtc", "LastBackupPath", "MinimumLogLevel", "NotificationMaxAttempts", "StaleAfterMinutes", "UpdatedAtUtc" },
                values: new object[] { 1, false, 365, null, null, "Information", 2, 15, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "ModemConfigurations",
                columns: new[] { "Id", "AutoConnect", "AutoReconnect", "BaudRate", "CommandTimeoutMs", "DataBits", "DeleteSmsAfterPersistence", "Handshake", "Parity", "PollIntervalSeconds", "PortName", "StopBits", "UpdatedAtUtc", "UseSimulatedModem" },
                values: new object[] { 1, true, true, 115200, 8000, 8, true, 0, 0, 30, "SIMULATOR", 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimestampUtc",
                table: "AuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationLogs_DeviceId",
                table: "CommunicationLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationLogs_Direction",
                table: "CommunicationLogs",
                column: "Direction");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationLogs_Kind",
                table: "CommunicationLogs",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationLogs_Outcome",
                table: "CommunicationLogs",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationLogs_TimestampUtc",
                table: "CommunicationLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_IsActive",
                table: "Contacts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_PhoneNumber",
                table: "Contacts",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GasDevices_DeviceCode",
                table: "GasDevices",
                column: "DeviceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GasDevices_ExpectedSenderNumber",
                table: "GasDevices",
                column: "ExpectedSenderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GasDevices_IsActive",
                table: "GasDevices",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GasDevices_LastSuccessfulCommunicationUtc",
                table: "GasDevices",
                column: "LastSuccessfulCommunicationUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_DedupKey",
                table: "Incidents",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_DeviceId",
                table: "Incidents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OccurredAtUtc",
                table: "Incidents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ReceivedAtUtc",
                table: "Incidents",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Severity",
                table: "Incidents",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Status",
                table: "Incidents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_TriggeringSmsMessageId",
                table: "Incidents",
                column: "TriggeringSmsMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_DeviceId",
                table: "SmsMessages",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Fingerprint",
                table: "SmsMessages",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_ProcessingStatus",
                table: "SmsMessages",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_ReceivedAtUtc",
                table: "SmsMessages",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_SenderNumberNormalized",
                table: "SmsMessages",
                column: "SenderNumberNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_SourceReference",
                table: "SmsMessages",
                column: "SourceReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPreferences");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CommunicationLogs");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "ModemConfigurations");

            migrationBuilder.DropTable(
                name: "SmsMessages");

            migrationBuilder.DropTable(
                name: "GasDevices");
        }
    }
}
