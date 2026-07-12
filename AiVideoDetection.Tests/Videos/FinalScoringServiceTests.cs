using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Videos.Ai;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class FinalScoringServiceTests
{
    private readonly FinalScoringService _service = new(Options.Create(new ScoringOptions()));

    [Fact]
    public void HighVisualScoreReturnsLikelyAiGeneratedWhenConfidenceIsEnough()
    {
        var result = _service.Calculate(new FinalScoringInput(1.00m, 0.85m, [], 12, null, "Likely AI-Generated"));

        Assert.Equal(AnalysisLabel.LikelyAiGenerated, result.Label);
    }

    [Fact]
    public void MediumScoreReturnsSuspicious()
    {
        var result = _service.Calculate(new FinalScoringInput(0.65m, 0.80m, ["missing_creation_time"], 12, null, "Suspicious"));

        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
    }

    [Fact]
    public void LowScoreReturnsLikelyReal()
    {
        var result = _service.Calculate(new FinalScoringInput(0.10m, 0.90m, [], 12, null, "Likely Real"));

        Assert.Equal(AnalysisLabel.LikelyReal, result.Label);
    }

    [Fact]
    public void LowConfidenceReducesStrongLabels()
    {
        var result = _service.Calculate(new FinalScoringInput(0.90m, 0.40m, [], 1, null, "Likely AI-Generated"));

        Assert.NotEqual(AnalysisLabel.LikelyAiGenerated, result.Label);
        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
    }

    [Fact]
    public void MetadataWarningsAffectMetadataScore()
    {
        var result = _service.Calculate(new FinalScoringInput(0.20m, 0.90m, ["missing_creation_time", "missing_encoder"], 12, null, null));

        Assert.True(result.MetadataScore > 0);
    }

    [Fact]
    public void MissingTemporalScoreDoesNotReduceFinalScoreUnfairly()
    {
        var result = _service.Calculate(new FinalScoringInput(0.80m, 0.80m, [], 12, null, null));

        Assert.True(result.FinalScore >= 0.70m);
        Assert.True(result.Label is AnalysisLabel.LikelyAiGenerated or AnalysisLabel.Suspicious);
    }

    [Fact]
    public void MetadataDoesNotOverpowerVisualScore()
    {
        var result = _service.Calculate(new FinalScoringInput(0.62m, 0.80m, [], 12, null, null));

        Assert.True(result.FinalScore >= 0.55m);
        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
    }

    [Fact]
    public void LowConfidencePrefersInconclusiveInsteadOfLikelyRealNearBoundary()
    {
        var result = _service.Calculate(new FinalScoringInput(0.25m, 0.30m, [], 12, null, null));

        Assert.Equal(AnalysisLabel.Inconclusive, result.Label);
    }

    [Fact]
    public void LikelyRealRequiresAdequateConfidence()
    {
        var result = _service.Calculate(new FinalScoringInput(0.10m, 0.50m, [], 12, null, null));

        Assert.Equal(AnalysisLabel.Inconclusive, result.Label);
    }

    [Fact]
    public void MinimumRecommendedVisualScoreCrossesSuspiciousThreshold()
    {
        var result = _service.Calculate(new FinalScoringInput(0.68m, 0.68m, [], 12, null, "Suspicious"));

        Assert.True(result.FinalScore >= 0.60m);
        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
    }

    [Fact]
    public void FrameHighVideoLowDisagreementPrefersInconclusive()
    {
        var result = _service.Calculate(new FinalScoringInput(
            0.51m,
            0.68m,
            ["missing_encoder"],
            30,
            null,
            "Suspicious",
            ModelDisagreement: true,
            VideoComponentScore: 0.44m));

        Assert.Equal(AnalysisLabel.Inconclusive, result.Label);
    }

    [Fact]
    public void RealFalsePositiveScenarioReturnsInconclusive()
    {
        var result = _service.Calculate(new FinalScoringInput(
            VisualScore: 0.51m,
            AiConfidence: 0.68m,
            MetadataWarnings: [],
            FrameCount: 30,
            TemporalScore: 0.44m,
            LabelHint: "Suspicious",
            ModelDisagreement: true,
            VideoComponentScore: 0.44m,
            FrameRawScore: 0.84m,
            FrameCalibratedScore: 0.64m));

        Assert.Equal(AnalysisLabel.Inconclusive, result.Label);
        Assert.Contains(result.Warnings, warning => warning.Contains("temporal video model did not confirm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AiFalseNegativeScenarioDoesNotReturnLikelyRealWhenStrongReliableFrameSignalExists()
    {
        var result = _service.Calculate(new FinalScoringInput(
            VisualScore: 0.53m,
            AiConfidence: 0.75m,
            MetadataWarnings: [],
            FrameCount: 30,
            TemporalScore: 0.44m,
            LabelHint: "Suspicious",
            ModelDisagreement: true,
            VideoComponentScore: 0.44m,
            FrameRawScore: 0.90m,
            FrameCalibratedScore: 0.66m,
            StrongFrameEvidence: true,
            DetectorReliabilityScore: 0.70m));

        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
    }

    [Fact]
    public void StrongAgreementAiCanReturnLikelyAiGenerated()
    {
        var result = _service.Calculate(new FinalScoringInput(
            VisualScore: 0.72m,
            AiConfidence: 0.82m,
            MetadataWarnings: [],
            FrameCount: 30,
            TemporalScore: 0.70m,
            LabelHint: "Likely AI-Generated",
            VideoComponentScore: 0.70m,
            FrameRawScore: 0.78m,
            FrameCalibratedScore: 0.75m));

        Assert.True(result.Label is AnalysisLabel.LikelyAiGenerated or AnalysisLabel.Suspicious);
    }

    [Fact]
    public void StrongAgreementRealReturnsLikelyReal()
    {
        var result = _service.Calculate(new FinalScoringInput(
            VisualScore: 0.18m,
            AiConfidence: 0.85m,
            MetadataWarnings: [],
            FrameCount: 30,
            TemporalScore: 0.15m,
            LabelHint: "Likely Real",
            VideoComponentScore: 0.15m,
            FrameRawScore: 0.20m,
            FrameCalibratedScore: 0.20m));

        Assert.Equal(AnalysisLabel.LikelyReal, result.Label);
    }

    [Fact]
    public void ModelDisagreementReducesConfidenceAndPreventsStrongRealLabel()
    {
        var result = _service.Calculate(new FinalScoringInput(
            VisualScore: 0.35m,
            AiConfidence: 0.80m,
            MetadataWarnings: [],
            FrameCount: 30,
            TemporalScore: 0.20m,
            LabelHint: "Inconclusive",
            VideoComponentScore: 0.20m,
            FrameRawScore: 0.55m,
            FrameCalibratedScore: 0.45m));

        Assert.Equal(AnalysisLabel.Inconclusive, result.Label);
        Assert.True(result.Confidence < 0.80m);
        Assert.Contains(result.Warnings, warning => warning.Contains("disagree", StringComparison.OrdinalIgnoreCase));
    }
}
