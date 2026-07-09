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
}
