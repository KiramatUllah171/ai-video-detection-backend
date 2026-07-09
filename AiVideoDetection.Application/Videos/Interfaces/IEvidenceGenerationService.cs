using AiVideoDetection.Application.Videos.Ai;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IEvidenceGenerationService
{
    IReadOnlyList<CreateEvidenceItemDto> GenerateEvidence(EvidenceGenerationInput input);
}
