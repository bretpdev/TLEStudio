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

    [MaxLength(256)]
    public string? Email { get; set; }

    public bool IsEmailVerified { get; set; } = true;

    [MaxLength(128)]
    public string? EmailVerificationTokenHash { get; set; }

    public DateTime? EmailVerificationTokenExpiresUtc { get; set; }

    [MaxLength(128)]
    public string? PasswordResetTokenHash { get; set; }

    public DateTime? PasswordResetTokenExpiresUtc { get; set; }

    [Required]
    [MaxLength(32)]
    public string AccountState { get; set; } = LoginAccountStates.Active;

    public bool IsAdmin { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
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

    public DateTime? CanceledUtc { get; set; }

    [MaxLength(100)]
    public string? CanceledByUserName { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DayAvailabilityOverride
{
    public int Id { get; set; }

    public DateOnly OverrideDate { get; set; }
}
