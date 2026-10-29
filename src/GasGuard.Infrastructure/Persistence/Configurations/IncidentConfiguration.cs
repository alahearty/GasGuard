using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeviceCodeSnapshot).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DeviceNameSnapshot).HasMaxLength(120).IsRequired();
        builder.Property(x => x.LocationSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Severity).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.OccurredTimeSource).HasConversion<int>();
        builder.Property(x => x.Unit).HasMaxLength(16);
        builder.Property(x => x.DedupKey).HasMaxLength(160).IsRequired();
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(100);
        builder.Property(x => x.AcknowledgementNotes).HasMaxLength(2000);
        builder.Property(x => x.ResolvedBy).HasMaxLength(100);
        builder.Property(x => x.ResolutionNotes).HasMaxLength(2000);

        builder.HasIndex(x => x.DedupKey).IsUnique();
        builder.HasIndex(x => x.TriggeringSmsMessageId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Severity);
        builder.HasIndex(x => x.ReceivedAtUtc);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.DeviceId);

        builder.HasOne(x => x.Device)
            .WithMany(x => x.Incidents)
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TriggeringSmsMessage)
            .WithOne(x => x.Incident)
            .HasForeignKey<Incident>(x => x.TriggeringSmsMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
