using AiVideoDetection.Api.Middleware;
using AiVideoDetection.Api.Options;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Api;

public class RequestBodySizeLimitMiddlewareTests
{
    [Fact]
    public async Task UploadAllowsMultipartHeadroomAboveBusinessFileLimit()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/videos/upload";
        context.Request.ContentLength = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes + 1024;

        await middleware.InvokeAsync(context, new TestCorrelationIdAccessor());

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task UploadRejectsBodyAboveBoundedMultipartHeadroom()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/videos/upload";
        context.Request.ContentLength = VideoUploadSizeLimits.MultipartRequestBodyLimitBytes + 1;

        await middleware.InvokeAsync(context, new TestCorrelationIdAccessor());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    private static RequestBodySizeLimitMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new RequestBodySizeLimitMiddleware(
            next,
            Options.Create(new RequestLimitOptions()));
    }

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string? CorrelationId { get; set; } = "test-correlation";
    }
}
