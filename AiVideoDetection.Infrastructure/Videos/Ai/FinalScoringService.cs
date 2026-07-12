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
        var videoScore = input.VideoComponentScore is null ? temporalScore : Clamp(input.VideoComponentScore.Value);
        var frameCalibratedScore = input.FrameCalibratedScore is null ? visualScore : Clamp(input.FrameCalibratedScore.Value);
        var frameRawScore = input.FrameRawScore is null ? frameCalibratedScore : Clamp(input.FrameRawScore.Value);

        var detectorScore = videoScore is null
            ? visualScore
            : WeightedAverage(
                [(videoScore.Value, _options.VideoDetectorMaxWeight), (frameCalibratedScore, _options.FrameDetectorMaxWeight)]);
        var finalScore = WeightedAverage([(detectorScore, _options.VisualWeight), (metadataScore, _options.MetadataWeight)]);

        var modelDisagreement = input.ModelDisagreement
            || (videoScore is not null && Math.Abs(videoScore.Value - frameRawScore) >= _options.ModelDisagreementThreshold);
        var strongFrameEvidence = input.StrongFrameEvidence
            || frameRawScore >= _options.StrongRawFrameThreshold
            || frameCalibratedScore >= _options.StrongCalibratedFrameThreshold;
        var confidence = CalculateConfidence(input.AiConfidence, input.MetadataWarnings.Count, input.FrameCount, modelDisagreement);
        var decision = CalculateLabel(
            finalScore,
            confidence,
            modelDisagreement,
            videoScore,
            frameRawScore,
            frameCalibratedScore,
            strongFrameEvidence,
            input.MetadataWarnings,
            input.DetectorReliabilityScore);

        return new FinalScoringResult(
            visualScore,
            metadataScore,
            temporalScore,
            Math.Round(finalScore, 3),
            Math.Round(confidence, 3),
            decision.Label,
            "This result is probability-based and generated using the current AI service output and available metadata signals. This is not a guarantee of authenticity or origin.",
            decision.Warnings);
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

    private decimal CalculateConfidence(decimal aiConfidence, int metadataWarningCount, int frameCount, bool modelDisagreement)
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
        if (modelDisagreement)
        {
            confidence -= 0.10m;
        }

        return Clamp(confidence);
    }

    private DecisionResult CalculateLabel(
        decimal finalScore,
        decimal confidence,
        bool modelDisagreement,
        decimal? videoScore,
        decimal frameRawScore,
        decimal frameCalibratedScore,
        bool strongFrameEvidence,
        IReadOnlyList<string> metadataWarnings,
        decimal? detectorReliabilityScore)
    {
        var warnings = new List<string>();
        var metadataSuspicious = metadataWarnings.Count > 0;
        var detectorReliabilityEstablished = detectorReliabilityScore is not null;
        var detectorReliabilityAllowsSingleSignal = !detectorReliabilityEstablished
            || detectorReliabilityScore >= _options.MinimumDetectorReliabilityForSingleDetectorSuspicious;
        var bothDetectorsHigh = videoScore >= 0.60m && frameCalibratedScore >= _options.StrongCalibratedFrameThreshold;
        var frameHighVideoLow = videoScore is < 0.50m
            && (frameCalibratedScore >= _options.StrongCalibratedFrameThreshold || frameRawScore >= 0.80m);
        var videoHighFrameLow = videoScore >= 0.65m && frameCalibratedScore < 0.50m;

        if (frameHighVideoLow)
        {
            warnings.Add("Frame detector found AI-like visual signals, but temporal video model did not confirm.");
            if (strongFrameEvidence
                && _options.AllowSingleDetectorSuspicious
                && detectorReliabilityAllowsSingleSignal
                && confidence >= 0.50m
                && frameRawScore >= _options.StrongRawFrameThreshold)
            {
                return new DecisionResult(AnalysisLabel.Suspicious, warnings);
            }

            return new DecisionResult(AnalysisLabel.Inconclusive, warnings);
        }

        if (videoHighFrameLow)
        {
            warnings.Add("Temporal detector found suspicious sequence-level signals, but frame detector did not confirm.");
            return new DecisionResult(AnalysisLabel.Suspicious, warnings);
        }

        if (finalScore >= _options.LikelyAiThreshold
            && confidence >= Math.Max(_options.MinimumConfidenceForStrongLabel, 0.65m)
            && (!_options.RequireAgreementForLikelyAi
                || videoScore is null
                || bothDetectorsHigh
                || (strongFrameEvidence && detectorReliabilityScore >= 0.75m)))
        {
            return new DecisionResult(AnalysisLabel.LikelyAiGenerated, warnings);
        }

        if (modelDisagreement)
        {
            warnings.Add("Model components disagree; treat result with caution.");
        }

        if (strongFrameEvidence || modelDisagreement || metadataSuspicious)
        {
            if (finalScore >= _options.SuspiciousThreshold
                || (strongFrameEvidence && _options.AllowSingleDetectorSuspicious && detectorReliabilityAllowsSingleSignal && confidence >= 0.50m))
            {
                return new DecisionResult(AnalysisLabel.Suspicious, warnings);
            }

            return new DecisionResult(AnalysisLabel.Inconclusive, warnings);
        }

        if (finalScore >= _options.SuspiciousThreshold)
        {
            return new DecisionResult(AnalysisLabel.Suspicious, warnings);
        }

        if (finalScore >= _options.InconclusiveThreshold)
        {
            return new DecisionResult(AnalysisLabel.Inconclusive, warnings);
        }

        if (confidence < 0.45m && finalScore >= 0.20m)
        {
            return new DecisionResult(AnalysisLabel.Inconclusive, warnings);
        }

        if (finalScore < 0.30m && !modelDisagreement && !strongFrameEvidence && !metadataSuspicious && confidence >= 0.60m)
        {
            return new DecisionResult(AnalysisLabel.LikelyReal, warnings);
        }

        return new DecisionResult(AnalysisLabel.Inconclusive, warnings);
    }

    private sealed record DecisionResult(AnalysisLabel Label, IReadOnlyList<string> Warnings);

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
