using AiVideoDetection.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace AiVideoDetection.Application.Videos.DTOs;

public class UploadVideoRequest
{
    public IFormFile? File { get; set; }

    public bool ConsentAccepted { get; set; }

    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Basic;

    public string? Notes { get; set; }
}
