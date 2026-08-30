using System.Net;

namespace AiVideoDetection.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }

    public long? UserId { get; set; }

    public User? User { get; set; }

    public string? UserName { get; set; }

    public string? UserEmail { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Severity { get; set; } = "Information";

    public string Message { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public string? HttpMethod { get; set; }

    public string? Path { get; set; }

    public int? StatusCode { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DetailsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
