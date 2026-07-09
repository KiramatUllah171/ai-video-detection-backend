namespace AiVideoDetection.Application.Videos.Processing;

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
