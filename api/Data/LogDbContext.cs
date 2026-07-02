using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class LogDbContext : DbContext
{
    public LogDbContext(DbContextOptions<LogDbContext> options)
        : base(options)
    {
    }

    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Timestamp).HasColumnName("TimestampUtc").IsRequired();
            entity.Property(x => x.Level).IsRequired().HasMaxLength(32);
            entity.Property(x => x.Message).IsRequired();
            entity.Property(x => x.MessageTemplate).HasMaxLength(4000);
            entity.Property(x => x.SourceContext).HasMaxLength(512);
            entity.Property(x => x.PropertiesJson).HasMaxLength(16000);
            entity.HasIndex(x => x.Timestamp);
            entity.HasIndex(x => x.Level);
        });
    }
}
