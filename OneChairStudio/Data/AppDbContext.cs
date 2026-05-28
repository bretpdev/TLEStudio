using Microsoft.EntityFrameworkCore;
using TLEStudio.Models;

namespace TLEStudio.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LoginUser> LoginUsers => Set<LoginUser>();
    public DbSet<AvailabilityHour> AvailabilityHours => Set<AvailabilityHour>();
    public DbSet<WeeklyAvailabilityRule> WeeklyAvailabilityRules => Set<WeeklyAvailabilityRule>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DayAvailabilityOverride> DayAvailabilityOverrides => Set<DayAvailabilityOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LoginUser>()
            .HasIndex(x => x.UserName)
            .IsUnique();

        modelBuilder.Entity<AvailabilityHour>()
            .HasIndex(x => x.SlotStart)
            .IsUnique();

        modelBuilder.Entity<WeeklyAvailabilityRule>()
            .HasIndex(x => x.DayOfWeekNumber)
            .IsUnique();

        modelBuilder.Entity<ServiceOffering>()
            .HasIndex(x => x.Name)
            .IsUnique();

        modelBuilder.Entity<ServiceOffering>()
            .Property(x => x.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Appointment>()
            .HasIndex(x => x.StartTime);

        modelBuilder.Entity<Appointment>()
            .HasIndex(x => x.ServiceOfferingId);

        modelBuilder.Entity<DayAvailabilityOverride>()
            .HasIndex(x => x.OverrideDate)
            .IsUnique();
    }
}
