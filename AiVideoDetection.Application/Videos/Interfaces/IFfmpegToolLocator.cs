using AiVideoDetection.Application.Videos.Processing;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IFfmpegToolLocator
{
    FfmpegToolResolution Resolve(string toolName, string configuredValue);
}
