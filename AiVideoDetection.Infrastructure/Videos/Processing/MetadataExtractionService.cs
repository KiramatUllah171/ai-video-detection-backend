using System.Globalization;
using System.Text.Json;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class MetadataExtractionService(
    IProcessRunner processRunner,
    IOptions<VideoProcessingOptions> options) : IMetadataExtractionService
{
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task<ExtractedMetadataResult> ExtractMetadataAsync(
        VideoProcessingInput input,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            input.SourceFilePath
        };

        var result = await processRunner.RunAsync("ffprobe", _options.FfprobePath, arguments, cancellationToken);
        if (!result.Succeeded)
        {
            throw new ProcessingException("FFPROBE_FAILED", "Video metadata could not be read.");
        }

        try
        {
            return ParseMetadata(result.StandardOutput);
        }
        catch (JsonException exception)
        {
            throw new ProcessingException("METADATA_EXTRACTION_FAILED", "Video metadata was unreadable.", exception);
        }
    }

    private static ExtractedMetadataResult ParseMetadata(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var warnings = new List<string>();

        JsonElement? format = root.TryGetProperty("format", out var formatElement)
            ? formatElement
            : null;

        var streams = root.TryGetProperty("streams", out var streamsElement) && streamsElement.ValueKind == JsonValueKind.Array
            ? streamsElement.EnumerateArray().ToArray()
            : [];

        var videoStream = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "video");
        var audioStream = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "audio");

        var codec = GetString(videoStream, "codec_name");
        var audioCodec = audioStream.ValueKind == JsonValueKind.Undefined ? null : GetString(audioStream, "codec_name");
        var width = GetInt(videoStream, "width");
        var height = GetInt(videoStream, "height");
        var resolution = width is not null && height is not null ? $"{width}x{height}" : null;
        var duration = ParseDecimal(GetString(format, "duration")) ?? ParseDecimal(GetString(videoStream, "duration"));
        var bitrate = ParseLong(GetString(format, "bit_rate"));
        var fps = ParseFps(GetString(videoStream, "avg_frame_rate")) ?? ParseFps(GetString(videoStream, "r_frame_rate"));
        var formatName = GetString(format, "format_name");

        var tags = format?.TryGetProperty("tags", out var tagsElement) == true
            ? tagsElement
            : default;

        var encoder = GetString(tags, "encoder");
        var creationTime = ParseDateTimeOffset(GetString(tags, "creation_time"));

        if (creationTime is null)
        {
            warnings.Add("missing_creation_time");
        }

        if (string.IsNullOrWhiteSpace(encoder))
        {
            warnings.Add("missing_encoder");
        }

        if (audioCodec is null)
        {
            warnings.Add("no_audio_stream");
        }

        if (bitrate is > 0 and < 100_000)
        {
            warnings.Add("unusually_low_bitrate");
        }

        if (duration is null || codec is null || resolution is null)
        {
            warnings.Add("missing_core_metadata");
        }

        return new ExtractedMetadataResult(
            formatName,
            codec,
            audioCodec,
            fps,
            resolution,
            duration,
            bitrate,
            encoder,
            creationTime,
            warnings.Count > 0,
            warnings,
            rawJson);
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        return element is { ValueKind: not JsonValueKind.Undefined }
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind != JsonValueKind.Undefined
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.ValueKind != JsonValueKind.Undefined
            && element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static long? ParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseFps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0")
        {
            return null;
        }

        var parts = value.Split('/', 2);
        if (parts.Length == 2
            && decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator)
            && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
        {
            return Math.Round(numerator / denominator, 3);
        }

        return ParseDecimal(value);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
