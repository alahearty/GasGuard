using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class AppPreferencesConfiguration : IEntityTypeConfiguration<AppPreferences>
{
    public void Configure(EntityTypeBuilder<AppPreferences> builder)
    {
        builder.ToTable("AppPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MinimumLogLevel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LastBackupPath).HasMaxLength(512);
        builder.HasData(new AppPreferences
        {
            Id = AppPreferences.SingletonId,
            StaleAfterMinutes = 15,
            DataRetentionDays = 365,
            AutomaticContactNotifications = false,
            NotificationMaxAttempts = 2,
            MinimumLogLevel = "Information",
            UpdatedAtUtc = ModemConfigurationConfiguration.SeedTimestamp
        });
    }
}
