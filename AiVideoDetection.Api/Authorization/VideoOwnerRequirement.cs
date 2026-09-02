using System.Security.Claims;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Api.Authorization;

public sealed class VideoOwnerRequirement(string resourceType) : IAuthorizationRequirement
{
    public string ResourceType { get; } = resourceType;
}

public sealed class VideoOwnerAuthorizationHandler(AppDbContext dbContext)
    : AuthorizationHandler<VideoOwnerRequirement, long>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        VideoOwnerRequirement requirement,
        long videoId)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var ownsVideo = await dbContext.Videos
            .AsNoTracking()
            .AnyAsync(video => video.Id == videoId
                && video.UserId == userId
                && video.DeletedAt == null
                && video.Status != VideoStatus.Deleted);

        if (ownsVideo)
        {
            context.Succeed(requirement);
        }
    }
}
