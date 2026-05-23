using System.ComponentModel.DataAnnotations;

namespace OneChairStudio.Models;

public sealed class LoginUser
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
