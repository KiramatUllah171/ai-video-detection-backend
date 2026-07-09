using AiVideoDetection.Application.Videos.Processing;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string toolName,
        string configuredFileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}
