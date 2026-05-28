using System.ComponentModel.DataAnnotations;

namespace TLEStudio.Models;

public sealed class LoginUser
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}

public sealed class AvailabilityHour
{
    public int Id { get; set; }

    public DateTime SlotStart { get; set; }

    public DateTime SlotEnd { get; set; }
}

public sealed class WeeklyAvailabilityRule
{
    public int Id { get; set; }

    public int DayOfWeekNumber { get; set; }

    public bool IsOpen { get; set; }

    public int StartHour { get; set; }

    public int EndHour { get; set; }
}

public sealed class ServiceOffering
{
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class Appointment
{
    public int Id { get; set; }

    public int ServiceOfferingId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ClientUserName { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    [Required]
    [MaxLength(40)]
    public string Status { get; set; } = "Booked";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DayAvailabilityOverride
{
    public int Id { get; set; }

    public DateOnly OverrideDate { get; set; }
}
