using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TLEStudio.Components;
using TLEStudio.Data;
using TLEStudio.Services;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TLEStudioAuth";
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-policy", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"auth:{ipAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["ConnectionStrings__DefaultConnection"]
    ?? builder.Configuration["DefaultConnection"]
    ?? throw new InvalidOperationException("DefaultConnection connection string not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }),
    ServiceLifetime.Scoped);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SeedData.Initialize(scope.ServiceProvider, db, app.Environment.IsDevelopment());
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapGet("/calendar-feed.ics", async (HttpContext http, AppDbContext db, IConfiguration config) =>
{
    var configuredToken = config["CalendarFeed:AccessToken"];
    if (!string.IsNullOrWhiteSpace(configuredToken))
    {
        var providedToken = http.Request.Query["token"].ToString();
        if (!string.Equals(providedToken, configuredToken, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }
    }

    var appointments = await db.Appointments
        .AsNoTracking()
        .Where(x => x.Status == "Booked")
        .Join(
            db.ServiceOfferings.AsNoTracking(),
            appt => appt.ServiceOfferingId,
            svc => svc.Id,
            (appt, svc) => new
            {
                appt.Id,
                appt.StartTime,
                appt.EndTime,
                appt.ClientUserName,
                ServiceName = svc.Name
            })
        .OrderBy(x => x.StartTime)
        .ToListAsync();

    static string ToIcsUtc(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    static string EscapeIcs(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    var nowUtc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
    var sb = new StringBuilder();

    sb.AppendLine("BEGIN:VCALENDAR");
    sb.AppendLine("VERSION:2.0");
    sb.AppendLine("PRODID:-//TLEStudio//Appointments//EN");
    sb.AppendLine("CALSCALE:GREGORIAN");
    sb.AppendLine("METHOD:PUBLISH");
    sb.AppendLine("X-WR-CALNAME:TLE Studio Appointments");

    foreach (var row in appointments)
    {
        var summary = EscapeIcs(row.ServiceName);
        var description = EscapeIcs($"Client: {row.ClientUserName}");
        var uid = $"appt-{row.Id}@tlestudio";

        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{nowUtc}");
        sb.AppendLine($"DTSTART:{ToIcsUtc(row.StartTime)}");
        sb.AppendLine($"DTEND:{ToIcsUtc(row.EndTime)}");
        sb.AppendLine($"SUMMARY:{summary}");
        sb.AppendLine($"DESCRIPTION:{description}");
        sb.AppendLine("END:VEVENT");
    }

    sb.AppendLine("END:VCALENDAR");

    return Results.Text(sb.ToString(), "text/calendar; charset=utf-8");
});

app.MapGet("/appointments/{appointmentId:int}/download.ics", async (int appointmentId, HttpContext http, AppDbContext db) =>
{
    var userName = http.User.Identity?.Name;
    var isAdmin = http.User.IsInRole("Admin") ||
                  http.User.Claims.Any(x => x.Type == ClaimTypes.Role && x.Value == "Admin");

    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.Unauthorized();
    }

    var row = await db.Appointments
        .AsNoTracking()
        .Where(x => x.Id == appointmentId)
        .Where(x => x.Status == "Booked")
        .Join(
            db.ServiceOfferings.AsNoTracking(),
            appt => appt.ServiceOfferingId,
            svc => svc.Id,
            (appt, svc) => new
            {
                appt.Id,
                appt.StartTime,
                appt.EndTime,
                appt.ClientUserName,
                ServiceName = svc.Name
            })
        .FirstOrDefaultAsync();

    if (row is null)
    {
        return Results.NotFound();
    }

    if (!isAdmin && !string.Equals(row.ClientUserName, userName, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    static string ToIcsUtc(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    static string EscapeIcs(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    var nowUtc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
    var sb = new StringBuilder();

    sb.AppendLine("BEGIN:VCALENDAR");
    sb.AppendLine("VERSION:2.0");
    sb.AppendLine("PRODID:-//TLEStudio//SingleAppointment//EN");
    sb.AppendLine("CALSCALE:GREGORIAN");
    sb.AppendLine("METHOD:PUBLISH");
    sb.AppendLine("BEGIN:VEVENT");
    sb.AppendLine($"UID:appt-{row.Id}@tlestudio");
    sb.AppendLine($"DTSTAMP:{nowUtc}");
    sb.AppendLine($"DTSTART:{ToIcsUtc(row.StartTime)}");
    sb.AppendLine($"DTEND:{ToIcsUtc(row.EndTime)}");
    sb.AppendLine($"SUMMARY:{EscapeIcs(row.ServiceName)}");
    sb.AppendLine($"DESCRIPTION:{EscapeIcs($"Client: {row.ClientUserName}")}");
    sb.AppendLine("END:VEVENT");
    sb.AppendLine("END:VCALENDAR");

    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
    var fileName = $"tlestudio-appointment-{row.StartTime:yyyyMMdd-HHmm}.ics";

    return Results.File(bytes, "text/calendar; charset=utf-8", fileName);
}).RequireAuthorization();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();