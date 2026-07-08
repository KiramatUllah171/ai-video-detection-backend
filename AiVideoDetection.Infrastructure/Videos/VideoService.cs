using System.Security.Cryptography;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiVideoDetection.Infrastructure.Videos;

public class VideoService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IValidator<UploadVideoRequest> uploadValidator,
    ILogger<VideoService> logger) : IVideoService
{
    public async Task<ApiResponse<UploadVideoResponse>> UploadAsync(
        UploadVideoRequest request,
        long currentUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var file = request.File!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var originalName = SanitizeOriginalName(file.FileName);
        var objectKey = CreateObjectKey(currentUserId, extension);
        string? uploadedObjectKey = null;
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");

        try
        {
            string sha256Hash;
            await using (var tempFile = File.Create(tempFilePath))
            await using (var input = file.OpenReadStream())
            using (var sha256 = SHA256.Create())
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await tempFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha256.TransformFinalBlock([], 0, 0);
                sha256Hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            }

            if (string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Video upload accepted with application/octet-stream for user id {UserId}.", currentUserId);
            }

            await using (var uploadStream = File.OpenRead(tempFilePath))
            {
                uploadedObjectKey = await objectStorageService.UploadAsync(
                    uploadStream,
                    objectKey,
                    file.ContentType,
                    cancellationToken);
            }

            var video = new Video
            {
                UserId = currentUserId,
                OriginalName = originalName,
                FileUrl = uploadedObjectKey,
                ContentType = file.ContentType,
                FileExtension = extension,
                FileSize = file.Length,
                Sha256Hash = sha256Hash,
                Status = VideoStatus.Uploaded
            };

            var job = new AnalysisJob
            {
                Video = video,
                Status = JobStatus.Queued,
                Progress = 0,
                CurrentStep = "Waiting for processing worker",
                RetryCount = 0,
                MaxRetryCount = 3
            };

            if (dbContext.Database.IsRelational())
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                new UploadVideoResponse
                {
                    VideoId = video.Id,
                    JobId = job.Id,
                    Status = video.Status.ToString(),
                    JobStatus = job.Status.ToString(),
                    OriginalName = video.OriginalName,
                    FileSize = video.FileSize,
                    ContentType = video.ContentType ?? string.Empty,
                    Message = "Video uploaded and queued for processing."
                },
                "Video uploaded successfully.");
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(uploadedObjectKey))
            {
                await objectStorageService.DeleteAsync(uploadedObjectKey, cancellationToken);
            }

            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public async Task<ApiResponse<PagedResponse<VideoHistoryItemDto>>> GetHistoryAsync(
        long currentUserId,
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Videos
            .AsNoTracking()
            .Where(video => video.UserId == currentUserId && video.DeletedAt == null && video.Status != VideoStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VideoStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(video => video.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(video => video.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(video => new VideoHistoryItemDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                FileSize = video.FileSize,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                LatestJobId = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => (long?)job.Id)
                    .FirstOrDefault(),
                LatestJobStatus = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => job.Status.ToString())
                    .FirstOrDefault(),
                LatestJobProgress = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => (int?)job.Progress)
                    .FirstOrDefault(),
                CurrentStep = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => job.CurrentStep)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<VideoHistoryItemDto>>.SuccessResponse(new PagedResponse<VideoHistoryItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    public async Task<ApiResponse<VideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .AsNoTracking()
            .Include(existingVideo => existingVideo.AnalysisJobs)
            .Where(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return video is null
            ? ApiResponse<VideoDetailDto>.ErrorResponse("Video was not found.")
            : ApiResponse<VideoDetailDto>.SuccessResponse(new VideoDetailDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                FileSize = video.FileSize,
                DurationSeconds = video.DurationSeconds,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                UpdatedAt = video.UpdatedAt,
                LatestJob = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(MapJob)
                    .FirstOrDefault()
            });
    }

    public async Task<ApiResponse<bool>> DeleteVideoAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<bool>.ErrorResponse("Video was not found.");
        }

        video.Status = VideoStatus.Deleted;
        video.DeletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Video deleted successfully.");
    }

    internal static JobStatusDto MapJob(AnalysisJob job)
    {
        return new JobStatusDto
        {
            JobId = job.Id,
            VideoId = job.VideoId,
            Status = job.Status.ToString(),
            Progress = job.Progress,
            CurrentStep = job.CurrentStep,
            ErrorMessage = job.ErrorMessage,
            ErrorCode = job.ErrorCode,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };
    }

    private static string CreateObjectKey(long userId, string extension)
    {
        var now = DateTimeOffset.UtcNow;
        return $"videos/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
    }

    private static string SanitizeOriginalName(string originalName)
    {
        var fileName = Path.GetFileName(originalName);
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName.Length <= 500 ? fileName : fileName[..500];
    }
}
