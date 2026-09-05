using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class FfmpegServiceTests
{
    [Fact]
    public async Task MetadataExtractionParsesFfprobeJson()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(0, """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "h264", "width": 1280, "height": 720, "avg_frame_rate": "30000/1001" },
                { "codec_type": "audio", "codec_name": "aac" },
                { "codec_type": "subtitle", "codec_name": "mov_text" },
                { "codec_type": "attachment", "codec_name": "png" }
              ],
              "format": {
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
                "duration": "12.500",
                "bit_rate": "800000",
                "tags": { "encoder": "Lavf", "creation_time": "2026-07-09T06:00:00Z" }
              }
            }
            """, "")
        };
        var service = new MetadataExtractionService(runner, Options.Create(new VideoProcessingOptions()));

        var result = await service.ExtractMetadataAsync(CreateInput(), CancellationToken.None);

        Assert.Equal("h264", result.Codec);
        Assert.Equal("aac", result.AudioCodec);
        Assert.Equal("1280x720", result.Resolution);
        Assert.Equal(12.500m, result.DurationSeconds);
        Assert.Equal(800000, result.Bitrate);
        Assert.Equal(29.970m, result.Fps);
        Assert.Equal(1, result.VideoStreamCount);
        Assert.Equal(1, result.AudioStreamCount);
        Assert.Equal(1, result.SubtitleStreamCount);
        Assert.Equal(1, result.AttachmentStreamCount);
        Assert.Equal(4, result.TotalStreamCount);
        Assert.False(result.HasMissingMetadata);
        Assert.Equal("ffprobe", runner.ToolName);
        Assert.Equal("ffprobe", runner.ConfiguredFileName);
        Assert.Contains("-show_streams", runner.Arguments);
    }

    [Fact]
    public async Task MetadataExtractionThrowsStableErrorWhenFfprobeFails()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult(1, "", "invalid data")
        };
        var service = new MetadataExtractionService(runner, Options.Create(new VideoProcessingOptions()));

        var exception = await Assert.ThrowsAsync<ProcessingException>(
            () => service.ExtractMetadataAsync(CreateInput(), CancellationToken.None));

        Assert.Equal("FFPROBE_FAILED", exception.ErrorCode);
    }

    [Fact]
    public async Task FrameExtractionUsesConfiguredFfmpegArguments()
    {
        var runner = new FakeProcessRunner { CreateFrameOutputs = true };
        var options = Options.Create(new VideoProcessingOptions
        {
            FrameIntervalSeconds = 3,
            MaxExtractedFrames = 2,
            FrameImageQuality = 4,
            FfmpegPath = "custom-ffmpeg"
        });
        var service = new FrameExtractionService(runner, options);
        var input = CreateInput();
        Directory.CreateDirectory(input.WorkingDirectory);

        var frames = await service.ExtractFramesAsync(input, CancellationToken.None);

        Assert.Equal(2, frames.Count);
        Assert.Equal("ffmpeg", runner.ToolName);
        Assert.Equal("custom-ffmpeg", runner.ConfiguredFileName);
        Assert.Contains("fps=1/3", runner.Arguments);
        Assert.Contains("-frames:v", runner.Arguments);
        Assert.Contains("2", runner.Arguments);
        Assert.Contains("-q:v", runner.Arguments);
        Assert.Contains("4", runner.Arguments);
    }

    [Fact]
    public async Task FrameExtractionRejectsOversizedFrameOutput()
    {
        var runner = new FakeProcessRunner
        {
            CreateFrameOutputs = true,
            FramePayload = [1, 2]
        };
        var options = Options.Create(new VideoProcessingOptions
        {
            MaxFrameFileSizeBytes = 1
        });
        var service = new FrameExtractionService(runner, options);
        var input = CreateInput();
        Directory.CreateDirectory(input.WorkingDirectory);

        var exception = await Assert.ThrowsAsync<ProcessingException>(
            () => service.ExtractFramesAsync(input, CancellationToken.None));

        Assert.Equal("FRAME_EXTRACTION_RESOURCE_LIMIT_EXCEEDED", exception.ErrorCode);
    }

    private static VideoProcessingInput CreateInput()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "ai-video-ffmpeg-tests", Guid.NewGuid().ToString("N"));
        return new VideoProcessingInput(1, 2, Path.Combine(workingDirectory, "source.mp4"), workingDirectory, 12);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public ProcessRunResult Result { get; init; } = new(0, "", "");

        public bool CreateFrameOutputs { get; init; }

        public byte[] FramePayload { get; init; } = [1];

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string? ToolName { get; private set; }

        public string? ConfiguredFileName { get; private set; }

        public async Task<ProcessRunResult> RunAsync(
            string toolName,
            string configuredFileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            ToolName = toolName;
            ConfiguredFileName = configuredFileName;
            Arguments = arguments;

            if (CreateFrameOutputs)
            {
                var outputPattern = arguments[^1];
                var directory = Path.GetDirectoryName(outputPattern)!;
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, "frame_000001.jpg"), FramePayload, cancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(directory, "frame_000002.jpg"), FramePayload, cancellationToken);
            }

            return Result;
        }
    }
}
