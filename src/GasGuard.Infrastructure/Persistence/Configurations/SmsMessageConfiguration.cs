using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> builder)
    {
        builder.ToTable("SmsMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SenderNumberRaw).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SenderNumberNormalized).HasMaxLength(20);
        builder.Property(x => x.BodyRaw).HasColumnType("TEXT").IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(120);
        builder.Property(x => x.ProcessingStatus).HasConversion<int>();
        builder.Property(x => x.ProcessingDetail).HasMaxLength(1000);
        builder.Property(x => x.DeviceCode).HasMaxLength(32);
        builder.Property(x => x.ProtocolVersion).HasMaxLength(8);
        builder.Property(x => x.EventCode).HasConversion<int>();
        builder.Property(x => x.Severity).HasConversion<int>();
        builder.Property(x => x.Unit).HasMaxLength(16);

        builder.HasIndex(x => x.Fingerprint);
        builder.HasIndex(x => x.SourceReference);
        builder.HasIndex(x => x.ReceivedAtUtc);
        builder.HasIndex(x => x.ProcessingStatus);
        builder.HasIndex(x => x.SenderNumberNormalized);

        builder.HasOne(x => x.Device)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
