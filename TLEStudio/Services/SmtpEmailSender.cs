using System.Net;
using System.Net.Mail;

namespace TLEStudio.Services;

public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured()
    {
        var host = configuration["Email:Smtp:Host"];
        var port = configuration["Email:Smtp:Port"];
        var from = configuration["Email:Smtp:From"];

        return !string.IsNullOrWhiteSpace(host)
            && int.TryParse(port, out _)
            && !string.IsNullOrWhiteSpace(from);
    }

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string verificationUrl, CancellationToken cancellationToken = default)
    {
        var subject = "Verify your TLE Studio account";
        var body = $"""
Hello,

Please verify your account by clicking the link below:
{verificationUrl}

This link expires in 30 minutes.

If you did not request this, you can ignore this email.
""";

        return await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        var subject = "Reset your TLE Studio password";
        var body = $"""
Hello,

A password reset was requested for your TLE Studio account.
Use the link below to choose a new password:
{resetUrl}

This link expires in 30 minutes.

If you were not expecting this, you can ignore this email.
""";

        return await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    private async Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
        {
            logger.LogWarning("Email sender is not configured. Skipping verification email send.");
            return false;
        }

        var host = configuration["Email:Smtp:Host"]!;
        var from = configuration["Email:Smtp:From"]!;
        var user = configuration["Email:Smtp:User"];
        var password = configuration["Email:Smtp:Password"];

        _ = int.TryParse(configuration["Email:Smtp:Port"], out var parsedPort);
        var port = parsedPort > 0 ? parsedPort : 587;

        var enableSsl = true;
        if (bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var configuredSsl))
        {
            enableSsl = configuredSsl;
        }

        try
        {
            using var message = new MailMessage(from, toEmail, subject, body);
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = string.IsNullOrWhiteSpace(user)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(user, password)
            };

            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }
}
