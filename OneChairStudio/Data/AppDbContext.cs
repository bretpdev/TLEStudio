using Microsoft.EntityFrameworkCore;
using OneChairStudio.Models;

namespace OneChairStudio.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LoginUser> LoginUsers => Set<LoginUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LoginUser>()
            .HasIndex(x => x.UserName)
            .IsUnique();
    }
}
