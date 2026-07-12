using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Videos.Ai;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class EvidenceGenerationServiceTests
{
    private readonly EvidenceGenerationService _service = new(Options.Create(new ScoringOptions
    {
        MaxFrameEvidenceItems = 2
    }));

    [Fact]
    public void HighFrameScoreCreatesHighEvidence()
    {
        var evidence = _service.GenerateEvidence(CreateInput([Frame(1, 1, 1, 0.90m)]));

        Assert.Contains(evidence, item => item.Type == EvidenceType.AiFrameScore && item.Severity == EvidenceSeverity.High);
    }

    [Fact]
    public void MediumFrameScoreCreatesMediumEvidence()
    {
        var evidence = _service.GenerateEvidence(CreateInput([Frame(1, 1, 1, 0.60m)]));

        Assert.Contains(evidence, item => item.Type == EvidenceType.AiFrameScore && item.Severity == EvidenceSeverity.Medium);
    }

    [Fact]
    public void MetadataWarningCreatesMetadataWarningEvidence()
    {
        var evidence = _service.GenerateEvidence(CreateInput([], ["missing_creation_time"]));

        Assert.Contains(evidence, item => item.Type == EvidenceType.MetadataWarning);
    }

    [Fact]
    public void MockModelVersionCreatesSystemNote()
    {
        var evidence = _service.GenerateEvidence(CreateInput([]));

        Assert.Contains(evidence, item => item.Type == EvidenceType.SystemNote);
    }

    [Fact]
    public void FrameEvidenceCountIsLimited()
    {
        var evidence = _service.GenerateEvidence(CreateInput(
        [
            Frame(1, 1, 1, 0.90m),
            Frame(2, 2, 2, 0.88m),
            Frame(3, 3, 3, 0.86m)
        ]));

        Assert.Equal(2, evidence.Count(item => item.Type == EvidenceType.AiFrameScore));
    }

    [Fact]
    public void DetectorDisagreementCreatesEvidenceItem()
    {
        var evidence = _service.GenerateEvidence(CreateInput(
            [Frame(1, 1, 1, 0.90m)],
            modelDisagreement: true));

        Assert.Contains(evidence, item => item.Title == "Detector disagreement");
    }

    private static EvidenceGenerationInput CreateInput(
        IReadOnlyList<AiFrameAnalysisResult> frames,
        IReadOnlyList<string>? warnings = null,
        bool modelDisagreement = false)
    {
        return new EvidenceGenerationInput(
            "mock-video-ai-v1",
            frames,
            frames.ToDictionary(frame => frame.FrameId, frame => frame.FrameId),
            warnings ?? [],
            new FinalScoringResult(0.6m, 0.2m, null, 0.5m, 0.8m, AnalysisLabel.Suspicious, "summary", []),
            modelDisagreement);
    }

    private static AiFrameAnalysisResult Frame(long frameId, int frameIndex, decimal timestamp, decimal aiScore)
    {
        return new AiFrameAnalysisResult(frameId, frameIndex, timestamp, aiScore, 1m - aiScore, 0.80m, []);
    }
}
