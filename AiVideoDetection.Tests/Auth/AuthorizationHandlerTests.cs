using System.Security.Claims;
using AiVideoDetection.Api.Authorization;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Tests.Auth;

public class AuthorizationHandlerTests
{
    [Fact]
    public async Task ActiveConfirmedUserRejectsTokenWhenSecurityStampChanged()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "current-stamp"));
        await dbContext.SaveChangesAsync();
        var requirement = new ActiveConfirmedUserRequirement(requireConfirmedEmail: true);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(1, "old-stamp"),
            resource: null);
        var handler = new ActiveConfirmedUserAuthorizationHandler(dbContext);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ActiveConfirmedUserAcceptsCurrentSecurityStamp()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "current-stamp"));
        await dbContext.SaveChangesAsync();
        var requirement = new ActiveConfirmedUserRequirement(requireConfirmedEmail: true);
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(1, "current-stamp"),
            resource: null);
        var handler = new ActiveConfirmedUserAuthorizationHandler(dbContext);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task VideoOwnerRejectsAnotherUsersVideo()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "stamp-1"), CreateUser(2, "stamp-2"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed
        });
        await dbContext.SaveChangesAsync();
        var requirement = new VideoOwnerRequirement("Video");
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(2, "stamp-2"),
            resource: 10L);
        var handler = new VideoOwnerAuthorizationHandler(dbContext);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task VideoOwnerAcceptsCurrentUsersVideo()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "stamp-1"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed
        });
        await dbContext.SaveChangesAsync();
        var requirement = new VideoOwnerRequirement("Video");
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(1, "stamp-1"),
            resource: 10L);
        var handler = new VideoOwnerAuthorizationHandler(dbContext);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreatePrincipal(long userId, string securityStamp)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("security_stamp", securityStamp),
                new Claim("email_confirmed", "true")
            ],
            authenticationType: "test"));
    }

    private static User CreateUser(long id, string securityStamp)
    {
        return new User
        {
            Id = id,
            Name = $"User {id}",
            Email = $"user{id}@example.com",
            PasswordHash = "hash",
            SecurityStamp = securityStamp,
            Role = UserRole.User,
            IsActive = true,
            EmailConfirmed = true
        };
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
