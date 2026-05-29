using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using TLEStudio.Data;
using TLEStudio.Models;
using TLEStudio.Services;

namespace TLEStudio.Controllers;

[ApiController]
[Route("auth")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AuthController(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthController> logger) : Controller
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [HttpPost("login")]
    [EnableRateLimiting("auth-policy")]
    public async Task<IActionResult> Login(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string returnUrl = "/calendar")
    {
        var normalizedIdentifier = username.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedIdentifier) || string.IsNullOrWhiteSpace(password))
        {
            return Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        var account = await dbContext.LoginUsers
            .FirstOrDefaultAsync(x =>
                x.UserName.ToLower() == normalizedIdentifier ||
                (x.Email != null && x.Email.ToLower() == normalizedIdentifier));

        if (account is null)
        {
            logger.LogWarning("Failed login for unknown account identifier: {Identifier}", normalizedIdentifier);
            return Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (account.LockoutEndUtc.HasValue && account.LockoutEndUtc.Value > DateTime.UtcNow)
        {
            return Redirect($"/login?locked=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (string.Equals(account.AccountState, LoginAccountStates.Denied, StringComparison.OrdinalIgnoreCase))
        {
            return Redirect($"/login?denied=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (string.Equals(account.AccountState, LoginAccountStates.Suspended, StringComparison.OrdinalIgnoreCase))
        {
            return Redirect($"/login?suspended=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (string.Equals(account.AccountState, LoginAccountStates.PendingApproval, StringComparison.OrdinalIgnoreCase))
        {
            return Redirect($"/login?pending_approval=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (!account.IsEmailVerified)
        {
            return Redirect($"/login?unverified=1&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, account.Password))
        {
            account.FailedLoginCount += 1;
            if (account.FailedLoginCount >= MaxFailedAttempts)
            {
                account.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
            }

            await dbContext.SaveChangesAsync();
            logger.LogWarning("Failed login for account: {UserName}. Failed count: {Count}", account.UserName, account.FailedLoginCount);

            var errorParam = account.LockoutEndUtc.HasValue && account.LockoutEndUtc.Value > DateTime.UtcNow
                ? "locked=1"
                : "error=1";

            return Redirect($"/login?{errorParam}&returnUrl={Uri.EscapeDataString(SanitizeReturnUrl(returnUrl))}");
        }

        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;
        await dbContext.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(ClaimTypes.Role, account.IsAdmin ? "Admin" : "Client")
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

    [HttpPost("register")]
    [EnableRateLimiting("auth-policy")]
    public async Task<IActionResult> Register(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string confirmPassword,
        [FromForm] string returnUrl = "/calendar",
        [FromForm(Name = "website")] string honeypot = "",
        [FromForm(Name = "cf-turnstile-response")] string turnstileToken = "")
    {
        var safeReturnUrl = SanitizeReturnUrl(returnUrl);
        if (!IsRegistrationEnabled())
        {
            return Redirect($"/register?error=disabled&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(honeypot))
        {
            logger.LogWarning("Blocked bot-like registration payload.");
            return Redirect($"/register?success=1&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!IsValidEmail(normalizedEmail))
        {
            return Redirect($"/register?error=invalid_email&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        if (!IsStrongPassword(password))
        {
            return Redirect($"/register?error=weak_password&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return Redirect($"/register?error=password_mismatch&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        if (!await IsTurnstileValidAsync(turnstileToken))
        {
            return Redirect($"/register?error=captcha&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        var exists = await dbContext.LoginUsers.AnyAsync(x =>
            x.UserName.ToLower() == normalizedEmail ||
            (x.Email != null && x.Email.ToLower() == normalizedEmail));
        if (exists)
        {
            return Redirect($"/register?error=exists&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        var verificationToken = VerificationTokenUtility.GenerateToken();
        var verificationTokenHash = VerificationTokenUtility.ComputeSha256Hex(verificationToken);
        var verificationExpiry = DateTime.UtcNow.AddMinutes(30);

        var account = new LoginUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            IsEmailVerified = false,
            EmailVerificationTokenHash = verificationTokenHash,
            EmailVerificationTokenExpiresUtc = verificationExpiry,
            AccountState = LoginAccountStates.PendingApproval,
            IsAdmin = false,
            FailedLoginCount = 0,
            LockoutEndUtc = null,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.LoginUsers.Add(account);
        await dbContext.SaveChangesAsync();

        var verificationUrl = BuildVerificationUrl(verificationToken);
        var sent = await emailSender.SendVerificationEmailAsync(normalizedEmail, verificationUrl, HttpContext.RequestAborted);
        if (!sent)
        {
            dbContext.LoginUsers.Remove(account);
            await dbContext.SaveChangesAsync();
            logger.LogError("Registration aborted because verification email could not be sent for {Email}.", normalizedEmail);
            return Redirect($"/register?error=email_send_failed&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
        }

        logger.LogInformation("New account registered: {Email}. Verification email sent.", normalizedEmail);
        return Redirect($"/login?verify_pending=1&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Redirect("/login?verified=0");
        }

        var tokenHash = VerificationTokenUtility.ComputeSha256Hex(token);
        var account = await dbContext.LoginUsers
            .FirstOrDefaultAsync(x => x.EmailVerificationTokenHash == tokenHash);

        if (account is null)
        {
            return Redirect("/login?verified=0");
        }

        if (account.IsEmailVerified)
        {
            return Redirect("/login?verified=1");
        }

        if (!account.EmailVerificationTokenExpiresUtc.HasValue || account.EmailVerificationTokenExpiresUtc.Value < DateTime.UtcNow)
        {
            return Redirect("/login?verified=expired");
        }

        account.IsEmailVerified = true;
        account.EmailVerificationTokenHash = null;
        account.EmailVerificationTokenExpiresUtc = null;
        await dbContext.SaveChangesAsync();

        return Redirect(string.Equals(account.AccountState, LoginAccountStates.PendingApproval, StringComparison.OrdinalIgnoreCase)
            ? "/login?verified=pending_approval"
            : "/login?verified=1");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpPost("request-password-reset")]
    [EnableRateLimiting("auth-policy")]
    public async Task<IActionResult> RequestPasswordReset([FromForm] string email)
    {
        if (!emailSender.IsConfigured())
        {
            return Redirect("/reset-password?error=disabled");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!IsValidEmail(normalizedEmail))
        {
            return Redirect("/reset-password?error=invalid_email");
        }

        var account = await dbContext.LoginUsers.FirstOrDefaultAsync(x =>
            (x.Email != null && x.Email.ToLower() == normalizedEmail) ||
            x.UserName.ToLower() == normalizedEmail);

        if (account is not null && !string.IsNullOrWhiteSpace(account.Email))
        {
            var resetToken = VerificationTokenUtility.GenerateToken();
            account.PasswordResetTokenHash = VerificationTokenUtility.ComputeSha256Hex(resetToken);
            account.PasswordResetTokenExpiresUtc = DateTime.UtcNow.AddMinutes(30);
            await dbContext.SaveChangesAsync();

            var resetUrl = BuildResetPasswordUrl(resetToken);
            var sent = await emailSender.SendPasswordResetEmailAsync(account.Email, resetUrl, HttpContext.RequestAborted);
            if (!sent)
            {
                return Redirect("/reset-password?error=email_send_failed");
            }

            logger.LogInformation("Password reset email requested for {UserName}", account.UserName);
        }

        return Redirect("/reset-password?requested=1");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromForm] string token,
        [FromForm] string password,
        [FromForm] string confirmPassword)
    {
        var encodedToken = Uri.EscapeDataString(token ?? string.Empty);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Redirect("/reset-password?error=invalid");
        }

        if (!IsStrongPassword(password))
        {
            return Redirect($"/reset-password?token={encodedToken}&error=weak_password");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return Redirect($"/reset-password?token={encodedToken}&error=password_mismatch");
        }

        var tokenHash = VerificationTokenUtility.ComputeSha256Hex(token);
        var account = await dbContext.LoginUsers
            .FirstOrDefaultAsync(x => x.PasswordResetTokenHash == tokenHash);

        if (account is null)
        {
            return Redirect("/reset-password?error=invalid");
        }

        if (!account.PasswordResetTokenExpiresUtc.HasValue || account.PasswordResetTokenExpiresUtc.Value < DateTime.UtcNow)
        {
            return Redirect("/reset-password?error=expired");
        }

        account.Password = BCrypt.Net.BCrypt.HashPassword(password);
        account.PasswordResetTokenHash = null;
        account.PasswordResetTokenExpiresUtc = null;
        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Password reset completed for {UserName}", account.UserName);

        return Redirect("/login?reset=1");
    }

    private string BuildResetPasswordUrl(string token)
    {
        var encodedToken = Uri.EscapeDataString(token);
        return $"{Request.Scheme}://{Request.Host}/reset-password?token={encodedToken}";
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

    private static bool IsValidEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length is < 6 or > 256)
        {
            return false;
        }

        return input.Contains('@') && input.Count(x => x == '@') == 1;
    }

    private static bool IsStrongPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasSymbol = false;

        foreach (var ch in password)
        {
            if (char.IsUpper(ch))
            {
                hasUpper = true;
            }
            else if (char.IsLower(ch))
            {
                hasLower = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }
            else
            {
                hasSymbol = true;
            }
        }

        return hasUpper && hasLower && hasDigit && hasSymbol;
    }

    private async Task<bool> IsTurnstileValidAsync(string token)
    {
        var secretKey = configuration["Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            using var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", secretKey),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty)
            });

            using var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", form);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var payload = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turnstile verification failed due to an exception.");
            return false;
        }
    }

    private bool IsRegistrationEnabled()
    {
        return !string.IsNullOrWhiteSpace(configuration["Turnstile:SecretKey"]) && emailSender.IsConfigured();
    }

    private string BuildVerificationUrl(string token)
    {
        var encodedToken = Uri.EscapeDataString(token);
        return $"{Request.Scheme}://{Request.Host}/auth/verify-email?token={encodedToken}";
    }

}
