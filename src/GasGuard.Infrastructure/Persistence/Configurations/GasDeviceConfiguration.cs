using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class GasDeviceConfiguration : IEntityTypeConfiguration<GasDevice>
{
    public void Configure(EntityTypeBuilder<GasDevice> builder)
    {
        builder.ToTable("GasDevices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeviceCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.InstallationLocation).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExpectedSenderNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DetectorType).HasConversion<int>();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.MaintenanceNotes).HasMaxLength(2000);
        builder.Property(x => x.LastReportedState).HasConversion<int>();
        builder.Property(x => x.LastReadingUnit).HasMaxLength(16);
        builder.Property(x => x.LastCommunicationError).HasMaxLength(500);
        builder.Property(x => x.LastEventCode).HasConversion<int>();

        builder.HasIndex(x => x.DeviceCode).IsUnique();
        builder.HasIndex(x => x.ExpectedSenderNumber).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.LastSuccessfulCommunicationUtc);
    }
}
