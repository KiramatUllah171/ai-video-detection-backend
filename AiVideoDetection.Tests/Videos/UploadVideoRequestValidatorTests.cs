using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class UploadVideoRequestValidatorTests
{
    private readonly UploadVideoRequestValidator _validator = new(Options.Create(new VideoUploadOptions
    {
        MaxFileSizeBytes = 10,
        AllowedExtensions = [".mp4"],
        AllowedContentTypes = ["video/mp4", "application/octet-stream"]
    }));

    [Fact]
    public async Task RejectsMissingFile()
    {
        var result = await _validator.ValidateAsync(new UploadVideoRequest { ConsentAccepted = true });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "A video file is required.");
    }

    [Fact]
    public async Task RejectsConsentAcceptedFalse()
    {
        var result = await _validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile("sample.mp4", "video/mp4", 3),
            ConsentAccepted = false
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("right to upload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RejectsUnsupportedExtension()
    {
        var result = await _validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile("sample.exe", "video/mp4", 3),
            ConsentAccepted = true
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "The uploaded file extension is not supported.");
    }

    [Fact]
    public async Task RejectsInvalidMimeType()
    {
        var result = await _validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile("sample.mp4", "text/plain", 3),
            ConsentAccepted = true
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "The uploaded file content type is not supported.");
    }

    [Fact]
    public async Task RejectsFileLargerThanConfiguredLimit()
    {
        var result = await _validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile("sample.mp4", "video/mp4", 11),
            ConsentAccepted = true
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("maximum allowed size", StringComparison.OrdinalIgnoreCase));
    }

    private static IFormFile CreateFile(string fileName, string contentType, int bytes)
    {
        var stream = new MemoryStream(Enumerable.Repeat((byte)1, bytes).ToArray());
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
