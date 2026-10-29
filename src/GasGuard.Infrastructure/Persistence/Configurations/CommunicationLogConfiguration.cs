using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GasGuard.Infrastructure.Persistence.Configurations;

internal sealed class CommunicationLogConfiguration : IEntityTypeConfiguration<CommunicationLog>
{
    public void Configure(EntityTypeBuilder<CommunicationLog> builder)
    {
        builder.ToTable("CommunicationLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Direction).HasConversion<int>();
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Outcome).HasConversion<int>();
        builder.Property(x => x.Summary).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Detail).HasColumnType("TEXT");
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.Property(x => x.RecipientNumber).HasMaxLength(20);
        builder.Property(x => x.ProviderReference).HasMaxLength(64);

        builder.HasIndex(x => x.TimestampUtc);
        builder.HasIndex(x => x.Outcome);
        builder.HasIndex(x => x.Kind);
        builder.HasIndex(x => x.Direction);

        builder.HasOne(x => x.Device)
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
