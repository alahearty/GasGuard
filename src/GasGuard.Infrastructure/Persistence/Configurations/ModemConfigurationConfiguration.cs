using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class ModemConfigurationConfiguration : IEntityTypeConfiguration<ModemConfiguration>
{
    public static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<ModemConfiguration> builder)
    {
        builder.ToTable("ModemConfigurations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PortName).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Parity).HasConversion<int>();
        builder.Property(x => x.StopBits).HasConversion<int>();
        builder.Property(x => x.Handshake).HasConversion<int>();
        builder.HasData(new ModemConfiguration
        {
            Id = ModemConfiguration.SingletonId,
            UseSimulatedModem = true,
            PortName = "SIMULATOR",
            BaudRate = 115200,
            DataBits = 8,
            Parity = SerialParity.None,
            StopBits = SerialStopBits.One,
            Handshake = SerialHandshake.None,
            CommandTimeoutMs = 8000,
            PollIntervalSeconds = 30,
            AutoConnect = true,
            AutoReconnect = true,
            DeleteSmsAfterPersistence = true,
            UpdatedAtUtc = SeedTimestamp
        });
    }
}
