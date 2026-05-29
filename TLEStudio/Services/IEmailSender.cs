namespace TLEStudio.Services;

public interface IEmailSender
{
    Task<bool> SendVerificationEmailAsync(string toEmail, string verificationUrl, CancellationToken cancellationToken = default);

    Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default);

    bool IsConfigured();
}
