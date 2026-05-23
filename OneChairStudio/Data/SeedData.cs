using Microsoft.EntityFrameworkCore;

namespace OneChairStudio.Data;

public static class SeedData
{
    public static async Task Initialize(IServiceProvider serviceProvider, AppDbContext context)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        logger.LogInformation("Starting database initialization");

        await context.Database.EnsureCreatedAsync();

        var hasAnyUser = await context.LoginUsers.AnyAsync();
        if (!hasAnyUser)
        {
            logger.LogInformation("No login users found, seeding starter user");

            // Update these values to create your own initial login.
            var starter = new Models.LoginUser
            {
                UserName = "admin",
                Password = BCrypt.Net.BCrypt.HashPassword("admin123")
            };

            context.LoginUsers.Add(starter);
            await context.SaveChangesAsync();
            logger.LogInformation("Starter user seeded successfully");
        }
    }
}
