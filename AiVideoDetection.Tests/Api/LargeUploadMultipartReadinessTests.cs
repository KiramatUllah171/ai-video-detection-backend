using System.Reflection;
using System.Text;
using AiVideoDetection.Api.Controllers;
using AiVideoDetection.Api.Middleware;
using AiVideoDetection.Api.Options;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Api;

public class LargeUploadMultipartReadinessTests
{
    [Fact]
    public void UploadEndpointAttributesUseCentralMultipartRequestCap()
    {
        var uploadMethod = typeof(VideosController).GetMethod("Upload")
            ?? throw new InvalidOperationException("Upload action was not found.");

        var requestLimit = uploadMethod.GetCustomAttribute<RequestSizeLimitAttribute>();
        var formLimit = uploadMethod.GetCustomAttribute<RequestFormLimitsAttribute>();

        Assert.NotNull(requestLimit);
        Assert.NotNull(formLimit);
        Assert.Equal(
            VideoUploadSizeLimits.MultipartRequestBodyLimitBytes,
            ((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize);
        Assert.Equal(VideoUploadSizeLimits.MultipartRequestBodyLimitBytes, formLimit.MultipartBodyLengthLimit);
    }

    [Fact]
    public void GeneratedMultipartRequestWith300MiBFileFitsConfiguredHeadroom()
    {
        const string boundary = "----SachAIProductionSmokeBoundary";
        var requestBytes = EstimateMultipartUploadLength(
            boundary,
            VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes,
            "production-limit.mp4",
            "video/mp4",
            includeAnalysisMode: true,
            includeConsent: true);

        Assert.True(requestBytes > VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes);
        Assert.True(requestBytes <= VideoUploadSizeLimits.MultipartRequestBodyLimitBytes);
        Assert.True(VideoUploadSizeLimits.MultipartRequestBodyLimitBytes - requestBytes > 19 * 1_048_576);
    }

    [Fact]
    public async Task RealAspNetMultipartParserReadsUploadFormWithoutCustomByteArrayLoading()
    {
        const string boundary = "----SachAISmallMultipartBoundary";
        var body = CreateMultipartBody(
            boundary,
            fileBytes: Encoding.UTF8.GetBytes("small mp4 smoke payload"),
            fileName: "smoke.mp4",
            contentType: "video/mp4");

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);

        var form = await context.Request.ReadFormAsync(new FormOptions
        {
            MultipartBodyLengthLimit = VideoUploadSizeLimits.MultipartRequestBodyLimitBytes,
            MemoryBufferThreshold = 64 * 1024
        });

        Assert.Equal("Basic", form["AnalysisMode"]);
        Assert.Equal("true", form["ConsentAccepted"]);
        var file = Assert.Single(form.Files);
        Assert.Equal("file", file.Name);
        Assert.Equal("smoke.mp4", file.FileName);
        Assert.Equal("video/mp4", file.ContentType);
        Assert.Equal("small mp4 smoke payload".Length, file.Length);
    }

    [Fact]
    public async Task MiddlewareRejectsDeclaredMultipartRequestAboveTechnicalCap()
    {
        var nextCalled = false;
        var middleware = new RequestBodySizeLimitMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new RequestLimitOptions()));
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/videos/upload";
        context.Request.ContentLength = VideoUploadSizeLimits.MultipartRequestBodyLimitBytes + 1;

        await middleware.InvokeAsync(context, new TestCorrelationIdAccessor());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    private static long EstimateMultipartUploadLength(
        string boundary,
        long fileBytes,
        string fileName,
        string contentType,
        bool includeAnalysisMode,
        bool includeConsent)
    {
        long total = 0;
        if (includeAnalysisMode)
        {
            total += Encoding.UTF8.GetByteCount(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"AnalysisMode\"\r\n\r\nBasic\r\n");
        }

        if (includeConsent)
        {
            total += Encoding.UTF8.GetByteCount(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"ConsentAccepted\"\r\n\r\ntrue\r\n");
        }

        total += Encoding.UTF8.GetByteCount(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
        total += fileBytes;
        total += Encoding.UTF8.GetByteCount($"\r\n--{boundary}--\r\n");
        return total;
    }

    private static byte[] CreateMultipartBody(
        string boundary,
        byte[] fileBytes,
        string fileName,
        string contentType)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, $"--{boundary}\r\nContent-Disposition: form-data; name=\"AnalysisMode\"\r\n\r\nBasic\r\n");
        WriteAscii(stream, $"--{boundary}\r\nContent-Disposition: form-data; name=\"ConsentAccepted\"\r\n\r\ntrue\r\n");
        WriteAscii(stream, $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
        stream.Write(fileBytes);
        WriteAscii(stream, $"\r\n--{boundary}--\r\n");
        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string? CorrelationId { get; set; } = "test-correlation";
    }
}
