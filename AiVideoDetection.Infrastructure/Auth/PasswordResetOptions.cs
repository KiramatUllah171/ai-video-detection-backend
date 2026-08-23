namespace AiVideoDetection.Infrastructure.Auth;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 30;

    public int EmailConfirmationTokenLifetimeHours { get; set; } = 24;

    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    public string Provider { get; set; } = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public bool UseSsl { get; set; }

    public string Username { get; set; } = "kiramatullahcomputer@gmail.com";

    public string? Password { get; set; }

    public string SenderEmail { get; set; } = "kiramatullahcomputer@gmail.com";

    public string SenderName { get; set; } = "sachvideoai";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpEnableSsl { get; set; } = true;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    public string FromEmail { get; set; } = "no-reply@ai-video-detection.local";

    public string FromName { get; set; } = "sachvideoai";
}
