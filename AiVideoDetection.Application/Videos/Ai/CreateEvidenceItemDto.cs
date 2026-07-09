using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Application.Videos.Ai;

public sealed record CreateEvidenceItemDto(
    long? VideoFrameId,
    EvidenceType Type,
    EvidenceSeverity Severity,
    string Title,
    string Description,
    decimal? ScoreImpact,
    decimal? TimestampSeconds);
