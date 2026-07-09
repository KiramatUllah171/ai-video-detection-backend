using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Enums;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Ai;

public class FinalScoringService(IOptions<ScoringOptions> options) : IFinalScoringService
{
    private readonly ScoringOptions _options = options.Value;

    public FinalScoringResult Calculate(FinalScoringInput input)
    {
        var visualScore = Clamp(input.VisualScore);
        var metadataScore = CalculateMetadataScore(input.MetadataWarnings);
        decimal? temporalScore = input.TemporalScore is null ? null : Clamp(input.TemporalScore.Value);

        var finalScore = temporalScore is null
            ? WeightedAverage(
                [(visualScore, _options.VisualWeight), (metadataScore, _options.MetadataWeight)])
            : WeightedAverage(
                [(visualScore, _options.VisualWeight), (metadataScore, _options.MetadataWeight), (temporalScore.Value, _options.TemporalWeight)]);

        var confidence = CalculateConfidence(input.AiConfidence, input.MetadataWarnings.Count, input.FrameCount);
        var label = CalculateLabel(finalScore, confidence);

        return new FinalScoringResult(
            visualScore,
            metadataScore,
            temporalScore,
            Math.Round(finalScore, 3),
            Math.Round(confidence, 3),
            label,
            "This result is probability-based and generated using the current AI service output and available metadata signals. This is not a guarantee of authenticity or origin.");
    }

    private decimal CalculateMetadataScore(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return 0m;
        }

        var score = warnings.Sum(warning => warning switch
        {
            "unreadable_metadata" => 0.55m,
            "unusually_low_bitrate" => 0.40m,
            "missing_core_metadata" => 0.35m,
            "missing_creation_time" => 0.20m,
            "missing_encoder" => 0.15m,
            "no_audio_stream" => 0.10m,
            _ => 0.15m
        });

        return Math.Min(score, 0.80m);
    }

    private decimal CalculateConfidence(decimal aiConfidence, int metadataWarningCount, int frameCount)
    {
        var confidence = Clamp(aiConfidence);
        if (frameCount < 3)
        {
            confidence -= 0.15m;
        }
        else if (frameCount < 8)
        {
            confidence -= 0.05m;
        }

        confidence -= Math.Min(metadataWarningCount * 0.03m, 0.15m);
        return Clamp(confidence);
    }

    private AnalysisLabel CalculateLabel(decimal finalScore, decimal confidence)
    {
        if (finalScore >= _options.LikelyAiThreshold && confidence >= _options.MinimumConfidenceForStrongLabel)
        {
            return AnalysisLabel.LikelyAiGenerated;
        }

        if (finalScore >= _options.SuspiciousThreshold)
        {
            return AnalysisLabel.Suspicious;
        }

        if (finalScore >= _options.InconclusiveThreshold)
        {
            return AnalysisLabel.Inconclusive;
        }

        return AnalysisLabel.LikelyReal;
    }

    private static decimal WeightedAverage(IReadOnlyList<(decimal Score, decimal Weight)> weightedScores)
    {
        var totalWeight = weightedScores.Sum(item => item.Weight);
        return totalWeight <= 0
            ? 0
            : weightedScores.Sum(item => item.Score * item.Weight) / totalWeight;
    }

    private static decimal Clamp(decimal value)
    {
        return Math.Clamp(value, 0m, 1m);
    }
}
