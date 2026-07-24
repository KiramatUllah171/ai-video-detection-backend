using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Application.Videos.Validators;

public class UploadVideoRequestValidator : AbstractValidator<UploadVideoRequest>
{
    private static readonly IReadOnlyDictionary<string, string[]> ExtensionContentTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ["video/mp4"],
        [".mov"] = ["video/quicktime", "video/mov", "video/x-quicktime"],
        [".avi"] = ["video/x-msvideo", "video/avi", "video/msvideo"],
        [".mkv"] = ["video/x-matroska", "video/matroska", "video/mkv", "video/x-mkv", "application/x-matroska"],
        [".webm"] = ["video/webm"]
    };

    private readonly VideoUploadOptions _options;

    public UploadVideoRequestValidator(IOptions<VideoUploadOptions> options)
    {
        _options = options.Value;

        RuleFor(request => request.File)
            .NotNull()
            .WithMessage("A video file is required.")
            .DependentRules(() =>
            {
                RuleFor(request => request.File!.Length)
                    .GreaterThan(0)
                    .WithMessage("The uploaded file is empty.")
                    .LessThanOrEqualTo(_options.MaxFileSizeBytes)
                    .WithMessage("The maximum allowed video size is 500 MB.");

                RuleFor(request => request.File!)
                    .Must(HaveAllowedExtension)
                    .WithMessage("The uploaded file extension is not supported.")
                    .Must(HaveSafeFileName)
                    .WithMessage("The uploaded filename is invalid.")
                    .Must(HaveAllowedContentType)
                    .WithMessage("The uploaded file content type is not supported.");
            });

        RuleFor(request => request.ConsentAccepted)
            .Equal(true)
            .WithMessage("You must confirm that you have the right to upload this video for analysis.");

        RuleFor(request => request.Notes)
            .MaximumLength(1000);
    }

    private bool HaveAllowedExtension(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        return !string.IsNullOrWhiteSpace(extension)
            && _options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HaveSafeFileName(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var fileName = file.FileName;
        return !string.IsNullOrWhiteSpace(fileName)
            && fileName == Path.GetFileName(fileName)
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private bool HaveAllowedContentType(Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (string.IsNullOrWhiteSpace(file.ContentType))
        {
            return false;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var configuredContentTypeIsAllowed = _options.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase);
        if (!configuredContentTypeIsAllowed)
        {
            return false;
        }

        if (string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ExtensionContentTypes.TryGetValue(extension, out var allowedForExtension)
            && allowedForExtension.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase);
    }
}
