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

    [Theory]
    [InlineData("sample.avi", "video/avi")]
    [InlineData("sample.avi", "video/msvideo")]
    [InlineData("sample.avi", "video/x-msvideo")]
    [InlineData("sample.mkv", "video/x-matroska")]
    [InlineData("sample.mkv", "video/matroska")]
    [InlineData("sample.mkv", "video/mkv")]
    [InlineData("sample.mkv", "video/x-mkv")]
    [InlineData("sample.mkv", "application/x-matroska")]
    [InlineData("sample.mkv", "application/octet-stream")]
    public async Task AcceptsConfiguredVideoContainerMimeAliases(string fileName, string contentType)
    {
        var validator = new UploadVideoRequestValidator(Options.Create(new VideoUploadOptions
        {
            MaxFileSizeBytes = 10,
            AllowedExtensions = [".avi", ".mkv"],
            AllowedContentTypes =
            [
                "video/x-msvideo",
                "video/avi",
                "video/msvideo",
                "video/x-matroska",
                "video/matroska",
                "video/mkv",
                "video/x-mkv",
                "application/x-matroska",
                "application/octet-stream"
            ]
        }));

        var result = await validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile(fileName, contentType, 3),
            ConsentAccepted = true
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task RejectsContentTypeThatDoesNotMatchExtension()
    {
        var validator = new UploadVideoRequestValidator(Options.Create(new VideoUploadOptions
        {
            MaxFileSizeBytes = 10,
            AllowedExtensions = [".mp4", ".mkv"],
            AllowedContentTypes = ["video/mp4", "video/x-matroska"]
        }));

        var result = await validator.ValidateAsync(new UploadVideoRequest
        {
            File = CreateFile("sample.mp4", "video/x-matroska", 3),
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
        Assert.Contains(result.Errors, error => error.ErrorMessage == "The maximum allowed video size is 500 MB.");
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
