using Microsoft.EntityFrameworkCore;
using TLEStudio.Models;

namespace TLEStudio.Data;

public static class SeedData
{
    public static async Task Initialize(IServiceProvider serviceProvider, AppDbContext context)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        logger.LogInformation("Starting database initialization");

        await context.Database.EnsureCreatedAsync();
        await EnsureSchemaAsync(context, logger);

        var hasAnyUser = await context.LoginUsers.AnyAsync();
        if (!hasAnyUser)
        {
            logger.LogInformation("No login users found, seeding starter users");

            // Update these values to create your own initial logins.
            var admin = new LoginUser
            {
                UserName = "admin",
                Password = BCrypt.Net.BCrypt.HashPassword("admin123"),
                IsAdmin = true
            };

            var client = new LoginUser
            {
                UserName = "client",
                Password = BCrypt.Net.BCrypt.HashPassword("client123"),
                IsAdmin = false
            };

            context.LoginUsers.Add(admin);
            context.LoginUsers.Add(client);
            await context.SaveChangesAsync();
            logger.LogInformation("Starter users seeded successfully");
        }

        var hasAdminUser = await context.LoginUsers.AnyAsync(x => x.IsAdmin);
        if (!hasAdminUser)
        {
            logger.LogInformation("No admin user found, ensuring default admin account exists");

            var existingAdminByName = await context.LoginUsers
                .FirstOrDefaultAsync(x => x.UserName.ToLower() == "admin");

            if (existingAdminByName is not null)
            {
                existingAdminByName.IsAdmin = true;
            }
            else
            {
                context.LoginUsers.Add(new LoginUser
                {
                    UserName = "admin",
                    Password = BCrypt.Net.BCrypt.HashPassword("admin123"),
                    IsAdmin = true
                });
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Admin account is available");
        }

        var bretUser = await context.LoginUsers.FirstOrDefaultAsync(x => x.UserName.ToLower() == "bret");
        if (bretUser is not null && !bretUser.IsAdmin)
        {
            logger.LogInformation("Promoting bret account to admin");
            bretUser.IsAdmin = true;
            await context.SaveChangesAsync();
        }

        var clientUser = await context.LoginUsers.FirstOrDefaultAsync(x => x.UserName.ToLower() == "client");
        if (clientUser is null)
        {
            logger.LogInformation("No client user found, creating default client account");
            context.LoginUsers.Add(new LoginUser
            {
                UserName = "client",
                Password = BCrypt.Net.BCrypt.HashPassword("client123"),
                IsAdmin = false
            });
            await context.SaveChangesAsync();
        }
        else if (clientUser.IsAdmin)
        {
            logger.LogInformation("Client account had admin role, resetting to non-admin");
            clientUser.IsAdmin = false;
            await context.SaveChangesAsync();
        }

        var hasWeeklyRules = await context.WeeklyAvailabilityRules.AnyAsync();
        if (!hasWeeklyRules)
        {
            logger.LogInformation("No weekly availability rules found, seeding defaults");

            for (var day = 1; day <= 6; day++)
            {
                context.WeeklyAvailabilityRules.Add(new WeeklyAvailabilityRule
                {
                    DayOfWeekNumber = day,
                    IsOpen = true,
                    StartHour = 10,
                    EndHour = 18
                });
            }

            await context.SaveChangesAsync();
        }

        var hasServices = await context.ServiceOfferings.AnyAsync();
        if (!hasServices)
        {
            logger.LogInformation("No services found, seeding default service catalog");

            context.ServiceOfferings.AddRange(
                new ServiceOffering { Name = "Haircut", Price = 55m, DurationMinutes = 60, IsActive = true },
                new ServiceOffering { Name = "Color", Price = 135m, DurationMinutes = 150, IsActive = true },
                new ServiceOffering { Name = "Cut + Color", Price = 170m, DurationMinutes = 180, IsActive = true },
                new ServiceOffering { Name = "Gloss / Toner", Price = 85m, DurationMinutes = 90, IsActive = true },
                new ServiceOffering { Name = "Event Styling", Price = 75m, DurationMinutes = 75, IsActive = true });

            await context.SaveChangesAsync();
        }

        var hasAvailability = await context.AvailabilityHours.AnyAsync();
        if (!hasAvailability)
        {
            logger.LogInformation("No availability found, applying weekly rules for one month");

            var today = DateOnly.FromDateTime(DateTime.Today);
            var rangeStart = StartOfBusinessWeek(today);
            var rangeEnd = today.AddMonths(1);

            var rules = await context.WeeklyAvailabilityRules
                .AsNoTracking()
                .ToListAsync();

            var rulesByDay = rules.ToDictionary(x => x.DayOfWeekNumber);

            for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                var dayNumber = (int)day.DayOfWeek;
                if (!rulesByDay.TryGetValue(dayNumber, out var rule) || !rule.IsOpen || rule.EndHour <= rule.StartHour)
                {
                    continue;
                }

                for (var hour = rule.StartHour; hour < rule.EndHour; hour++)
                {
                    var start = day.ToDateTime(new TimeOnly(hour, 0));
                    context.AvailabilityHours.Add(new AvailabilityHour
                    {
                        SlotStart = start,
                        SlotEnd = start.AddHours(1)
                    });
                }
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Availability seeded from weekly rules successfully");
        }
    }

    private static async Task EnsureSchemaAsync(AppDbContext context, ILogger logger)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('dbo.LoginUsers', 'IsAdmin') IS NULL
                BEGIN
                    ALTER TABLE dbo.LoginUsers
                    ADD IsAdmin bit NOT NULL CONSTRAINT DF_LoginUsers_IsAdmin DEFAULT(0);
                END;
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID('dbo.AvailabilityHours', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AvailabilityHours (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        SlotStart datetime2 NOT NULL,
                        SlotEnd datetime2 NOT NULL
                    );

                    CREATE UNIQUE INDEX IX_AvailabilityHours_SlotStart ON dbo.AvailabilityHours (SlotStart);
                END;
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID('dbo.WeeklyAvailabilityRules', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.WeeklyAvailabilityRules (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        DayOfWeekNumber int NOT NULL,
                        IsOpen bit NOT NULL,
                        StartHour int NOT NULL,
                        EndHour int NOT NULL
                    );

                    CREATE UNIQUE INDEX IX_WeeklyAvailabilityRules_DayOfWeekNumber ON dbo.WeeklyAvailabilityRules (DayOfWeekNumber);
                END;
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID('dbo.ServiceOfferings', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ServiceOfferings (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        Name nvarchar(120) NOT NULL,
                        Price decimal(18,2) NOT NULL,
                        DurationMinutes int NOT NULL,
                        IsActive bit NOT NULL CONSTRAINT DF_ServiceOfferings_IsActive DEFAULT(1)
                    );

                    CREATE UNIQUE INDEX IX_ServiceOfferings_Name ON dbo.ServiceOfferings (Name);
                END;
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID('dbo.Appointments', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Appointments (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        ServiceOfferingId int NOT NULL,
                        ClientUserName nvarchar(100) NOT NULL,
                        StartTime datetime2 NOT NULL,
                        EndTime datetime2 NOT NULL,
                        Status nvarchar(40) NOT NULL,
                        CreatedUtc datetime2 NOT NULL,
                        CONSTRAINT FK_Appointments_ServiceOfferings_ServiceOfferingId
                            FOREIGN KEY (ServiceOfferingId) REFERENCES dbo.ServiceOfferings(Id)
                    );

                    CREATE INDEX IX_Appointments_StartTime ON dbo.Appointments (StartTime);
                    CREATE INDEX IX_Appointments_ServiceOfferingId ON dbo.Appointments (ServiceOfferingId);
                END;
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID('dbo.DayAvailabilityOverrides', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.DayAvailabilityOverrides (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        OverrideDate date NOT NULL
                    );

                    CREATE UNIQUE INDEX IX_DayAvailabilityOverrides_OverrideDate ON dbo.DayAvailabilityOverrides (OverrideDate);
                END;
                """);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed ensuring schema changes for auth and availability.");
            throw;
        }
    }

    private static DateOnly StartOfBusinessWeek(DateOnly day)
    {
        var diff = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-diff);
    }
}

