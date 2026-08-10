namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>Shared HTML body for every password-reset email — used both for a self-service
/// "forgot password" request and for the link auto-sent when an account locks itself out.</summary>
public static class PasswordResetEmailTemplate
{
    public const string Subject = "Apple Esports - Password Reset";

    public static string Compose(string targetName, string resetLink)
    {
        return $@"
        <div style='background-color:#050505; color:#ffffff; font-family:""Segoe UI"", Tahoma, Geneva, Verdana, sans-serif; padding:40px 20px; text-align:center;'>
            <div style='max-width: 600px; margin: 0 auto; background-color: #111111; border: 1px solid #333333; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 20px rgba(0,0,0,0.5);'>
                <div style='background: linear-gradient(135deg, #1a1a24 0%, #0d0d14 100%); padding: 30px 20px; border-bottom: 2px solid #dc2626;'>
                    <h1 style='margin: 0; font-size: 28px; letter-spacing: 2px; color: #ffffff; text-transform: uppercase;'>
                        <img src='https://appleesports.in/apple-touch-icon.png' alt='Logo' style='height: 40px; vertical-align: middle; margin-right: 15px;' /> APPLE ESPORTS
                    </h1>
                </div>
                <div style='padding: 40px 30px; text-align: left;'>
                    <h2 style='margin-top: 0; color: #f87171; font-size: 24px; border-bottom: 2px solid #333333; padding-bottom: 15px;'>Password Reset Request</h2>
                    <p style='font-size: 16px; color: #d1d5db; line-height: 1.6;'>Hi <strong>{targetName}</strong>,</p>
                    <p style='font-size: 16px; color: #d1d5db; line-height: 1.6;'>We received a request to reset the password for your Apple Esports account. Click the secure link below to choose a new password:</p>
                    <div style='text-align:center; margin: 40px 0;'>
                        <a href='{resetLink}' style='background: linear-gradient(to right, #dc2626, #ef4444); color: #ffffff; padding: 16px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; font-size: 16px; letter-spacing: 1px; display: inline-block; box-shadow: 0 4px 15px rgba(220, 38, 38, 0.4);'>RESET MY PASSWORD</a>
                    </div>
                    <p style='color: #6b7280; font-size: 13px; margin-top: 30px;'>If you did not request this password reset, please ignore this email. This link will expire in exactly 1 hour for your security.</p>
                </div>
                <div style='background-color: #080808; padding: 20px; border-top: 1px solid #222222; text-align: center;'>
                    <p style='margin: 0; color: #6b7280; font-size: 12px;'>This is an automated security notification from Apple Esports ERP.</p>
                    <p style='margin: 5px 0 0 0; color: #4b5563; font-size: 11px;'>© {DateTime.UtcNow.Year} Apple Esports. All rights reserved.</p>
                </div>
            </div>
        </div>";
    }

    /// <summary>Copy shown when a lockout — rather than a self-service request — triggered the email.</summary>
    public static string ComposeForLockout(string targetName, string resetLink)
    {
        var body = Compose(targetName, resetLink);
        return body.Replace(
            "We received a request to reset the password for your Apple Esports account. Click the secure link below to choose a new password:",
            "Your account was locked after 5 incorrect password attempts. Click the secure link below to set a new password and unlock your account immediately:");
    }
}
