using System.Text.Json;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class JobLogService(AppDbContext dbContext) : IJobLogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(
        long jobId,
        string stepName,
        string level,
        string message,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.JobLogs.Add(new JobLog
        {
            JobId = jobId,
            StepName = stepName,
            Level = level,
            Message = message,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, SerializerOptions)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
