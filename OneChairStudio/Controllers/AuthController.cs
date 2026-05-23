using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OneChairStudio.Data;

namespace OneChairStudio.Controllers;

[ApiController]
[Route("auth")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AuthController(AppDbContext dbContext) : Controller
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string returnUrl = "/calendar")
    {
        var normalizedUserName = username.Trim().ToLowerInvariant();

        var account = await dbContext.LoginUsers
            .FirstOrDefaultAsync(x => x.UserName.ToLower() == normalizedUserName);

        if (account is null)
        {
            return Redirect("/login?error=1");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, account.Password))
        {
            return Redirect("/login?error=1");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(ClaimTypes.Role, "Client")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });

        return Redirect(SanitizeReturnUrl(returnUrl));
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    private static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/calendar";
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/calendar";
        }

        return uri.ToString();
    }
}
