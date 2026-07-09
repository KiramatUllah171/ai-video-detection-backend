namespace AiVideoDetection.Domain.Entities;

public class ModelVersion
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<AiResult> AiResults { get; set; } = [];
}
