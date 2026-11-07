using Microsoft.EntityFrameworkCore;

namespace Api.Db;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<Measurement> Measurements => Set<Measurement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Admin>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(50);
            e.HasIndex(x => x.Username).IsUnique();
        });

        b.Entity<Session>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Series>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Color).HasMaxLength(7).IsFixedLength();
            e.Property(x => x.Icon).HasMaxLength(30);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_series_name", "length(name) > 0");
                t.HasCheckConstraint("ck_series_range", "min_value < max_value");
                t.HasCheckConstraint("ck_series_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
            });
        });

        b.Entity<Sensor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.ApiKeyHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => x.ApiKeyHash).IsUnique();
            e.HasOne(x => x.Series).WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_sensors_name", "length(name) > 0"));
        });

        b.Entity<Measurement>(e =>
        {
            e.HasOne(x => x.Series).WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sensor).WithMany().HasForeignKey(x => x.SensorId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.SeriesId, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
        });
    }
}
