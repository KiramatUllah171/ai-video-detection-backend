using System.Globalization;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class FrameExtractionService(
    IProcessRunner processRunner,
    IOptions<VideoProcessingOptions> options) : IFrameExtractionService
{
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task<ThumbnailResult> GenerateThumbnailAsync(
        VideoProcessingInput input,
        CancellationToken cancellationToken)
    {
        var timestamp = ResolveThumbnailTimestamp(input.DurationSeconds);
        var extension = NormalizeFormat(_options.FrameOutputFormat);
        var outputPath = Path.Combine(input.WorkingDirectory, $"thumbnail.{extension}");

        var arguments = new[]
        {
            "-y",
            "-ss", FormatSeconds(timestamp),
            "-i", input.SourceFilePath,
            "-frames:v", "1",
            "-q:v", _options.FrameImageQuality.ToString(CultureInfo.InvariantCulture),
            outputPath
        };

        var result = await processRunner.RunAsync("ffmpeg", _options.FfmpegPath, arguments, cancellationToken);
        if (!result.Succeeded || !File.Exists(outputPath))
        {
            throw new ProcessingException("THUMBNAIL_FAILED", "Video thumbnail could not be generated.");
        }

        return new ThumbnailResult(outputPath, timestamp, null, null);
    }

    public async Task<IReadOnlyList<ExtractedFrameResult>> ExtractFramesAsync(
        VideoProcessingInput input,
        CancellationToken cancellationToken)
    {
        var interval = _options.FrameIntervalSeconds <= 0 ? 2 : _options.FrameIntervalSeconds;
        var maxFrames = Math.Max(_options.MaxExtractedFrames, 1);
        var extension = NormalizeFormat(_options.FrameOutputFormat);
        var frameDirectory = Path.Combine(input.WorkingDirectory, "frames");
        Directory.CreateDirectory(frameDirectory);

        var outputPattern = Path.Combine(frameDirectory, $"frame_%06d.{extension}");
        var fpsExpression = $"fps=1/{FormatSeconds(interval)}";

        var arguments = new[]
        {
            "-y",
            "-i", input.SourceFilePath,
            "-vf", fpsExpression,
            "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture),
            "-q:v", _options.FrameImageQuality.ToString(CultureInfo.InvariantCulture),
            outputPattern
        };

        var result = await processRunner.RunAsync("ffmpeg", _options.FfmpegPath, arguments, cancellationToken);
        if (!result.Succeeded)
        {
            throw new ProcessingException("FRAME_EXTRACTION_FAILED", "Video frames could not be extracted.");
        }

        return Directory
            .EnumerateFiles(frameDirectory, $"frame_*.{extension}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select((path, index) => new ExtractedFrameResult(
                path,
                index + 1,
                Math.Round(index * interval, 3),
                null,
                null,
                false))
            .ToList();
    }

    private decimal ResolveThumbnailTimestamp(decimal? durationSeconds)
    {
        var configured = _options.ThumbnailTimestampSeconds < 0 ? 0 : _options.ThumbnailTimestampSeconds;
        if (durationSeconds is null || durationSeconds <= 0)
        {
            return configured;
        }

        return Math.Min(configured, Math.Max(0, durationSeconds.Value / 2));
    }

    private static string NormalizeFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "jpg" : format.Trim().TrimStart('.').ToLowerInvariant();
        return normalized is "jpeg" ? "jpg" : normalized;
    }

    private static string FormatSeconds(decimal seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
