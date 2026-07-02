using api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class AppDbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<CostConfiguration> CostConfigurations => Set<CostConfiguration>();
    public DbSet<CostPriceHistory> CostPriceHistories => Set<CostPriceHistory>();
    public DbSet<SmsRecord> SmsRecords => Set<SmsRecord>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionNumber> SubscriptionNumbers => Set<SubscriptionNumber>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeDateTimesToLocal();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override int SaveChanges()
    {
        NormalizeDateTimesToLocal();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeDateTimesToLocal();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeDateTimesToLocal();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.KeyHash).IsRequired().HasMaxLength(256);
            entity.Property(x => x.Balance).HasPrecision(18, 4);
            entity.Property(x => x.ResponsibleName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.ResponsibleEmail).IsRequired().HasMaxLength(320);
            entity.Property(x => x.CreatedAt).HasColumnName("CreatedAtUtc");
            entity.Property(x => x.UpdatedAt).HasColumnName("UpdatedAtUtc");
            entity.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<CostConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SmsPriceDkk).HasPrecision(18, 4);
            entity.Property(x => x.UpdatedAt).HasColumnName("UpdatedAtUtc");
        });

        builder.Entity<CostPriceHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OldSmsPriceDkk).HasPrecision(18, 4);
            entity.Property(x => x.NewSmsPriceDkk).HasPrecision(18, 4);
            entity.Property(x => x.ChangedAt).HasColumnName("ChangedAtUtc");
            entity.HasIndex(x => x.ChangedAt);
        });

        builder.Entity<SmsRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Direction).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(30);
            entity.Property(x => x.ToPhoneNumber).HasMaxLength(32);
            entity.Property(x => x.FromPhoneNumber).HasMaxLength(32);
            entity.Property(x => x.Message).IsRequired();
            entity.Property(x => x.UnitPriceDkk).HasPrecision(18, 4);
            entity.Property(x => x.TotalPriceDkk).HasPrecision(18, 4);
            entity.Property(x => x.CreatedAt).HasColumnName("CreatedAtUtc");
            entity.Property(x => x.UpdatedAt).HasColumnName("UpdatedAtUtc");
            entity.Property(x => x.QueuedAt).HasColumnName("QueuedAtUtc");
            entity.Property(x => x.ProcessingStartedAt).HasColumnName("ProcessingStartedAtUtc");
            entity.Property(x => x.SentAt).HasColumnName("SentAtUtc");
            entity.Property(x => x.FailedAt).HasColumnName("FailedAtUtc");
            entity.Property(x => x.ReceivedAt).HasColumnName("ReceivedAtUtc");
            entity.Property(x => x.ModemResponseJson).HasMaxLength(16000);
            entity.Property(x => x.FailureReason).HasMaxLength(1000);
            entity.HasIndex(x => x.ApiKeyId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Direction);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.QueuedAt);
            entity.HasIndex(x => x.InboxIndex);
            entity.HasOne(x => x.ApiKey)
                .WithMany(x => x.SmsRecords)
                .HasForeignKey(x => x.ApiKeyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Subscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WebhookUrl).HasMaxLength(2048);
            entity.Property(x => x.CreatedAt).HasColumnName("CreatedAtUtc");
            entity.Property(x => x.UpdatedAt).HasColumnName("UpdatedAtUtc");
            entity.HasIndex(x => x.ApiKeyId);
            entity.HasIndex(x => new { x.StartDate, x.EndDate });
            entity.HasOne(x => x.ApiKey)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SubscriptionNumber>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => new { x.SubscriptionId, x.PhoneNumber }).IsUnique();
            entity.HasIndex(x => x.PhoneNumber);
            entity.HasOne(x => x.Subscription)
                .WithMany(x => x.Numbers)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void NormalizeDateTimesToLocal()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                var clrType = property.Metadata.ClrType;

                if (clrType == typeof(DateTime) && property.CurrentValue is DateTime dateTime)
                {
                    property.CurrentValue = ToLocalTime(dateTime);
                }
                else if (clrType == typeof(DateTime?) && property.CurrentValue is DateTime nullableDateTime)
                {
                    property.CurrentValue = ToLocalTime(nullableDateTime);
                }
            }
        }
    }

    private static DateTime ToLocalTime(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Utc)
        {
            return value.ToLocalTime();
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Local);
    }
}
