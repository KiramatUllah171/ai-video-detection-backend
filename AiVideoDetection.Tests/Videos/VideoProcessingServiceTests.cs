using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Matching;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Ai;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class VideoProcessingServiceTests
{
    [Fact]
    public async Task ValidProcessingMarksJobCompletedAndWritesAssets()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        var workRoot = CreateWorkRoot();
        var storage = new FakeObjectStorageService();
        var metadata = new FakeMetadataExtractionService();
        var frames = new FakeFrameExtractionService();
        var service = CreateService(dbContext, storage, metadata, frames, workRoot);

        await service.ProcessAnalysisJobAsync(job.Id);

        var savedJob = await dbContext.AnalysisJobs.Include(existingJob => existingJob.Video).SingleAsync();
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.Equal(100, savedJob.Progress);
        Assert.Equal(VideoStatus.Completed, savedJob.Video.Status);
        Assert.Contains("Analysis completed", savedJob.CurrentStep);
        Assert.True(metadata.SawProcessingStatus);
        Assert.Equal(3, storage.UploadCalls);
        Assert.Equal(1, await dbContext.MetadataResults.CountAsync());
        Assert.Equal(2, await dbContext.VideoFrames.CountAsync());
        Assert.Equal(1, await dbContext.AiResults.CountAsync());
        Assert.Equal(1, await dbContext.EvidenceItems.CountAsync());
        Assert.True(await dbContext.JobLogs.CountAsync() >= 4);
        Assert.False(Directory.Exists(Path.Combine(workRoot, job.Id.ToString())));
    }

    [Fact]
    public async Task MetadataFailureMarksJobFailedAndWritesErrorLog()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeMetadataExtractionService { ErrorCode = "FFPROBE_FAILED" },
            new FakeFrameExtractionService(),
            CreateWorkRoot());

        await Assert.ThrowsAsync<ProcessingException>(() => service.ProcessAnalysisJobAsync(job.Id));

        var savedJob = await dbContext.AnalysisJobs.Include(existingJob => existingJob.Video).SingleAsync();
        Assert.Equal(JobStatus.Failed, savedJob.Status);
        Assert.Equal(VideoStatus.Failed, savedJob.Video.Status);
        Assert.Equal("FFPROBE_FAILED", savedJob.ErrorCode);
        Assert.Contains("metadata", savedJob.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await dbContext.JobLogs.ToListAsync(), log => log.Level == "Error");
    }

    [Fact]
    public async Task CompletedJobIsNotProcessedAgain()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        job.Status = JobStatus.Completed;
        await dbContext.SaveChangesAsync();
        var metadata = new FakeMetadataExtractionService();
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            metadata,
            new FakeFrameExtractionService(),
            CreateWorkRoot());

        await service.ProcessAnalysisJobAsync(job.Id);

        Assert.Equal(0, metadata.CallCount);
    }

    [Fact]
    public async Task ReRunDoesNotDuplicateMetadataOrFrames()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        dbContext.MetadataResults.Add(new MetadataResult
        {
            VideoId = job.VideoId,
            Codec = "old",
            RawJson = "{}",
            WarningsJson = "[]"
        });
        dbContext.VideoFrames.Add(new VideoFrame
        {
            VideoId = job.VideoId,
            FrameIndex = 1,
            FrameUrl = "old",
            TimestampSeconds = 0
        });
        job.Status = JobStatus.Failed;
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeMetadataExtractionService(),
            new FakeFrameExtractionService(),
            CreateWorkRoot());

        await service.ProcessAnalysisJobAsync(job.Id);

        Assert.Equal(1, await dbContext.MetadataResults.CountAsync());
        Assert.Equal(2, await dbContext.VideoFrames.CountAsync());
        Assert.Equal(1, await dbContext.AiResults.CountAsync());
        Assert.Equal(1, await dbContext.EvidenceItems.CountAsync());
        Assert.Contains(await dbContext.VideoFrames.ToListAsync(), frame => frame.FrameIndex == 1 && frame.FrameUrl.Contains("frame_000001"));
    }

    [Fact]
    public async Task AiServiceFailureMarksJobFailed()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeMetadataExtractionService(),
            new FakeFrameExtractionService(),
            CreateWorkRoot(),
            new FailingAiInferenceClient());

        await Assert.ThrowsAsync<AiServiceException>(() => service.ProcessAnalysisJobAsync(job.Id));

        var savedJob = await dbContext.AnalysisJobs.Include(existingJob => existingJob.Video).SingleAsync();
        Assert.Equal(JobStatus.Failed, savedJob.Status);
        Assert.Equal(VideoStatus.Failed, savedJob.Video.Status);
        Assert.Equal("AI_SERVICE_UNAVAILABLE", savedJob.ErrorCode);
    }

    [Fact]
    public async Task BitMindModeSavesExternalScoreAndFinalDecisionFromConfidence()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeMetadataExtractionService(),
            new FakeFrameExtractionService(),
            CreateWorkRoot(),
            new FakeBitMindAiInferenceClient(),
            new AiServiceOptions
            {
                ProviderMode = "bitmind",
                BitMindEnabled = true,
                BitMindMonthlyQuota = 100,
                MaxFramesPerRequest = 30
            });

        await service.ProcessAnalysisJobAsync(job.Id);

        var result = await dbContext.AiResults.SingleAsync();
        Assert.Equal("BitMind", result.Provider);
        Assert.Equal("bitmind", result.ProviderMode);
        Assert.Equal("BitMind", result.FinalDecisionSource);
        Assert.Equal(0.8816881775856018m, result.ExternalScore);
        Assert.Equal(0.8816881775856018m, result.ExternalConfidence);
        Assert.Equal("LikelyAiGenerated", result.ExternalLabel);
        Assert.Equal(0.882m, result.FinalScore);
        Assert.Equal(0.882m, result.Confidence);
        Assert.Equal(AnalysisLabel.LikelyAiGenerated, result.Label);
        Assert.Contains("isAI", result.ExternalRawResponseJson);
        Assert.Contains("true", result.ExternalRawResponseJson);
    }

    [Fact]
    public async Task BitMindModeModerateAiConfidenceSavesSuspiciousNotLikelyAiGenerated()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedQueuedJobAsync(dbContext);
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeMetadataExtractionService(),
            new FakeFrameExtractionService(),
            CreateWorkRoot(),
            new FakeModerateBitMindAiInferenceClient(),
            new AiServiceOptions
            {
                ProviderMode = "bitmind",
                BitMindEnabled = true,
                BitMindMonthlyQuota = 100,
                MaxFramesPerRequest = 30
            });

        await service.ProcessAnalysisJobAsync(job.Id);

        var result = await dbContext.AiResults.SingleAsync();
        Assert.Equal("BitMind", result.Provider);
        Assert.Equal(0.7529020309448242m, result.ExternalScore);
        Assert.Equal(0.7529020309448242m, result.ExternalConfidence);
        Assert.Equal("Suspicious", result.ExternalLabel);
        Assert.Equal(0.753m, result.FinalScore);
        Assert.Equal(0.753m, result.Confidence);
        Assert.Equal(AnalysisLabel.Suspicious, result.Label);
        Assert.NotEqual(AnalysisLabel.LikelyAiGenerated, result.Label);
        var providerRequest = await dbContext.AiProviderRequests.SingleAsync();
        Assert.Contains("compression_used", providerRequest.RawRequestMetadataJson);
        Assert.Contains("provider_sent_file_name", providerRequest.RawRequestMetadataJson);
    }

    private static VideoProcessingService CreateService(
        AppDbContext dbContext,
        IObjectStorageService storage,
        IMetadataExtractionService metadata,
        IFrameExtractionService frames,
        string workRoot,
        IAiInferenceClient? aiClient = null,
        AiServiceOptions? aiOptions = null)
    {
        return new VideoProcessingService(
            dbContext,
            storage,
            metadata,
            frames,
            aiClient ?? new FakeAiInferenceClient(),
            new FakeFinalScoringService(),
            new FakeEvidenceGenerationService(),
            new FakeFrameHashService(),
            new FakeInternalVideoMatchingService(),
            new JobLogService(dbContext),
            Options.Create(new VideoProcessingOptions { WorkingRootPath = workRoot }),
            Options.Create(new InternalMatchingOptions { Enabled = true, FailJobOnMatchingError = false }),
            Options.Create(aiOptions ?? new AiServiceOptions { MaxFramesPerRequest = 30 }),
            NullLogger<VideoProcessingService>.Instance);
    }

    private static async Task<AnalysisJob> SeedQueuedJobAsync(AppDbContext dbContext)
    {
        var user = new User
        {
            Id = 1,
            Name = "Owner",
            Email = "owner@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true
        };
        var video = new Video
        {
            Id = 10,
            User = user,
            OriginalName = "sample.mp4",
            FileUrl = "videos/1/sample.mp4",
            FileExtension = ".mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded
        };
        var job = new AnalysisJob
        {
            Id = 20,
            Video = video,
            Status = JobStatus.Queued,
            Progress = 0,
            MaxRetryCount = 3
        };

        dbContext.Users.Add(user);
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync();
        return job;
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static string CreateWorkRoot()
    {
        return Path.Combine(Path.GetTempPath(), "ai-video-processing-tests", Guid.NewGuid().ToString("N"));
    }

    private sealed class FakeObjectStorageService : IObjectStorageService
    {
        public int UploadCalls { get; private set; }

        public Task<string> UploadAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            return Task.FromResult(objectKey);
        }

        public Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(objectKeyOrUrl);
        }

        public async Task DownloadToAsync(string objectKeyOrUrl, string destinationPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, [1, 2, 3], cancellationToken);
        }
    }

    private sealed class FakeMetadataExtractionService : IMetadataExtractionService
    {
        public int CallCount { get; private set; }

        public bool SawProcessingStatus { get; private set; }

        public string? ErrorCode { get; init; }

        public async Task<ExtractedMetadataResult> ExtractMetadataAsync(VideoProcessingInput input, CancellationToken cancellationToken)
        {
            CallCount++;
            await using var dbContext = CreateDbContext();
            SawProcessingStatus = true;

            if (ErrorCode is not null)
            {
                throw new ProcessingException(ErrorCode, "Video metadata could not be read.");
            }

            return new ExtractedMetadataResult(
                "mov,mp4,m4a,3gp,3g2,mj2",
                "h264",
                "aac",
                30,
                "1920x1080",
                6,
                500_000,
                "Lavf",
                null,
                true,
                ["missing_creation_time"],
                "{\"format\":{}}");
        }
    }

    private sealed class FakeFrameExtractionService : IFrameExtractionService
    {
        public async Task<ThumbnailResult> GenerateThumbnailAsync(VideoProcessingInput input, CancellationToken cancellationToken)
        {
            var path = Path.Combine(input.WorkingDirectory, "thumbnail.jpg");
            await File.WriteAllBytesAsync(path, [1], cancellationToken);
            return new ThumbnailResult(path, 1, 1920, 1080);
        }

        public async Task<IReadOnlyList<ExtractedFrameResult>> ExtractFramesAsync(VideoProcessingInput input, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(input.WorkingDirectory, "frames");
            Directory.CreateDirectory(directory);
            var first = Path.Combine(directory, "frame_000001.jpg");
            var second = Path.Combine(directory, "frame_000002.jpg");
            await File.WriteAllBytesAsync(first, [1], cancellationToken);
            await File.WriteAllBytesAsync(second, [2], cancellationToken);

            return
            [
                new ExtractedFrameResult(first, 1, 0, 1920, 1080, false),
                new ExtractedFrameResult(second, 2, 2, 1920, 1080, false)
            ];
        }
    }

    private sealed class FakeAiInferenceClient : IAiInferenceClient
    {
        public Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
            AiAnalyzeFramesRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiAnalyzeFramesResponse(
                request.VideoId,
                request.JobId,
                "mock-deterministic-frame-hash",
                "mock-video-ai-v1",
                "mock",
                true,
                0.62m,
                0.38m,
                0.78m,
                "Suspicious",
                request.Frames.Select(frame => new AiFrameAnalysisResult(
                    frame.FrameId,
                    frame.FrameIndex,
                    frame.TimestampSeconds,
                    0.64m,
                    0.36m,
                    0.81m,
                    ["Mock score generated from deterministic frame identifier hash."])).ToList(),
                ["This is a mock AI response for pipeline integration only."],
                ["Mock model was used. This is not real AI detection."]));
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
            AiAnalyzeVideoRequest request,
            CancellationToken cancellationToken = default)
        {
            return AnalyzeFramesAsync(
                new AiAnalyzeFramesRequest(request.VideoId, request.JobId, request.Frames),
                cancellationToken);
        }
    }

    private sealed class FailingAiInferenceClient : IAiInferenceClient
    {
        public Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
            AiAnalyzeFramesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new AiServiceException("AI_SERVICE_UNAVAILABLE", "AI analysis service is currently unavailable. Please try again later.");
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
            AiAnalyzeVideoRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new AiServiceException("AI_SERVICE_UNAVAILABLE", "AI analysis service is currently unavailable. Please try again later.");
        }
    }

    private sealed class FakeBitMindAiInferenceClient : IAiInferenceClient
    {
        public Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
            AiAnalyzeFramesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("BitMind mode should use AnalyzeVideoAsync.");
        }

        public Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
            AiAnalyzeVideoRequest request,
            CancellationToken cancellationToken = default)
        {
            const string externalProviderResult = """
            {
              "provider_name": "BitMind",
              "provider_label": "LikelyAiGenerated",
              "provider_score": 0.8816881775856018,
              "provider_job_id": null,
              "provider_status": "Completed",
              "provider_confidence": 0.8816881775856018,
              "provider_request_id": null,
              "provider_started_at": "2026-07-12T07:36:04.530939+00:00",
              "provider_completed_at": "2026-07-12T07:36:16.004705+00:00",
              "provider_raw_response": {
                "isAI": true,
                "objectKey": "1783841775771.mp4",
                "confidence": 0.8816881775856018,
                "similarity": 0,
                "thumbnailObjectKey": "thumbnails/1783841775771/main.jpg"
              },
              "provider_error_message": null
            }
            """;
            return Task.FromResult(new AiAnalyzeFramesResponse(
                request.VideoId,
                request.JobId,
                "bitmind-subnet-34",
                "bitmind-oracle-v1-sn34",
                "external_video",
                false,
                0.8816881775856018m,
                0.1183118224143982m,
                0.8816881775856018m,
                "LikelyAiGenerated",
                [],
                ["External BitMind video detection completed."],
                ["This video may be processed by an external AI detection provider for analysis."],
                Provider: "BitMind",
                ProviderMode: "bitmind",
                ExternalProviderResultJson: externalProviderResult,
                FinalDecisionSource: "BitMind")
            {
                RawJson = $$"""
                {
                  "video_id": {{request.VideoId}},
                  "job_id": {{request.JobId}},
                  "provider": "BitMind",
                  "provider_mode": "bitmind",
                  "final_decision_source": "BitMind",
                  "model_id": "bitmind-subnet-34",
                  "model_version": "bitmind-oracle-v1-sn34",
                  "model_capability": "external_video",
                  "is_mock": false,
                  "overall_ai_score": 0.8816881775856018,
                  "real_probability": 0.1183118224143982,
                  "overall_confidence": 0.8816881775856018,
                  "label_hint": "LikelyAiGenerated",
                  "frames": [],
                  "notes": ["External BitMind video detection completed."],
                  "warnings": ["This video may be processed by an external AI detection provider for analysis."],
                  "external_provider_result": {{externalProviderResult}}
                }
                """
            });
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeModerateBitMindAiInferenceClient : IAiInferenceClient
    {
        public Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
            AiAnalyzeFramesRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("BitMind mode should use AnalyzeVideoAsync.");
        }

        public Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
            AiAnalyzeVideoRequest request,
            CancellationToken cancellationToken = default)
        {
            const string externalProviderResult = """
            {
              "provider_name": "BitMind",
              "provider_label": "Suspicious",
              "provider_score": 0.7529020309448242,
              "provider_job_id": null,
              "provider_status": "Completed",
              "provider_confidence": 0.7529020309448242,
              "provider_request_id": null,
              "provider_decision_band": "ai_moderate_confidence",
              "original_file_size_bytes": 524288000,
              "bitmind_file_size_bytes": 104857600,
              "compression_used": true,
              "compression_attempts": 1,
              "compression_error": null,
              "analysis_copy_path": "C:\\temp\\bitmind_analysis_source.mp4",
              "provider_sent_file_name": "bitmind_analysis_source.mp4",
              "provider_started_at": "2026-07-12T07:36:04.530939+00:00",
              "provider_completed_at": "2026-07-12T07:36:16.004705+00:00",
              "provider_raw_response": {
                "isAI": true,
                "objectKey": "1783843392037.mp4",
                "confidence": 0.7529020309448242,
                "similarity": 0,
                "thumbnailObjectKey": "thumbnails/1783843392037/main.jpg"
              },
              "provider_error_message": null
            }
            """;
            return Task.FromResult(new AiAnalyzeFramesResponse(
                request.VideoId,
                request.JobId,
                "bitmind-subnet-34",
                "bitmind-oracle-v1-sn34",
                "external_video",
                false,
                0.7529020309448242m,
                0.2470979690551758m,
                0.7529020309448242m,
                "Suspicious",
                [],
                ["External BitMind video detection completed.", "BitMind detected AI-like signals, but confidence is moderate. Treat this as suspicious, not definitive."],
                ["BitMind detected AI-like signals, but confidence is moderate. This is suspicious, not definitive."],
                Provider: "BitMind",
                ProviderMode: "bitmind",
                ExternalProviderResultJson: externalProviderResult,
                FinalDecisionSource: "BitMind")
            {
                RawJson = $$"""
                {
                  "video_id": {{request.VideoId}},
                  "job_id": {{request.JobId}},
                  "provider": "BitMind",
                  "provider_mode": "bitmind",
                  "final_decision_source": "BitMind",
                  "model_id": "bitmind-subnet-34",
                  "model_version": "bitmind-oracle-v1-sn34",
                  "model_capability": "external_video",
                  "is_mock": false,
                  "overall_ai_score": 0.7529020309448242,
                  "real_probability": 0.2470979690551758,
                  "overall_confidence": 0.7529020309448242,
                  "label_hint": "Suspicious",
                  "frames": [],
                  "notes": ["External BitMind video detection completed."],
                  "warnings": ["BitMind detected AI-like signals, but confidence is moderate. This is suspicious, not definitive."],
                  "external_provider_result": {{externalProviderResult}}
                }
                """
            });
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeFinalScoringService : IFinalScoringService
    {
        public FinalScoringResult Calculate(FinalScoringInput input)
        {
            return new FinalScoringResult(
                input.VisualScore,
                0.20m,
                null,
                0.55m,
                input.AiConfidence,
                AnalysisLabel.Suspicious,
                "This result is probability-based and generated using the current AI service output and available metadata signals. This is not a guarantee of authenticity or origin.",
                []);
        }
    }

    private sealed class FakeEvidenceGenerationService : IEvidenceGenerationService
    {
        public IReadOnlyList<CreateEvidenceItemDto> GenerateEvidence(EvidenceGenerationInput input)
        {
            return
            [
                new CreateEvidenceItemDto(
                    null,
                    EvidenceType.SystemNote,
                    EvidenceSeverity.Low,
                    "Mock AI service result",
                    "This analysis was generated by the mock AI service for pipeline integration and should not be treated as a real AI detection result.",
                    null,
                    null)
            ];
        }
    }

    private sealed class FakeFrameHashService : IFrameHashService
    {
        public Task<IReadOnlyList<FrameHashResult>> GenerateHashesForVideoAsync(long videoId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<FrameHashResult> results =
            [
                new FrameHashResult(1, videoId, "0000000000000000", "1111111111111111", "2222222222222222", "mvp-v1")
            ];
            return Task.FromResult(results);
        }

        public Task<FrameHashResult> GenerateHashForFrameAsync(VideoFrame frame, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FrameHashResult(frame.Id, frame.VideoId, "0000000000000000", "1111111111111111", "2222222222222222", "mvp-v1"));
        }
    }

    private sealed class FakeInternalVideoMatchingService : IInternalVideoMatchingService
    {
        public Task<IReadOnlyList<InternalVideoMatchResult>> MatchVideoAsync(long videoId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<InternalVideoMatchResult>>([]);
        }

        public Task<ApiResponse<IReadOnlyList<SourceMatchDto>>> GetMatchesAsync(long videoId, long currentUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<SourceMatchDto>>.SuccessResponse([]));
        }
    }
}
