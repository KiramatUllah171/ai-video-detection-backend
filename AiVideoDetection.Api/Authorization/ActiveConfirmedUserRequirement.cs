using System.Security.Claims;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Api.Authorization;

public sealed class ActiveConfirmedUserRequirement(bool requireConfirmedEmail) : IAuthorizationRequirement
{
    public bool RequireConfirmedEmail { get; } = requireConfirmedEmail;
}

public sealed class ActiveConfirmedUserAuthorizationHandler(AppDbContext dbContext)
    : AuthorizationHandler<ActiveConfirmedUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveConfirmedUserRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenSecurityStamp = context.User.FindFirstValue("security_stamp");
        if (!long.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(tokenSecurityStamp))
        {
            return;
        }

        var userState = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.IsActive,
                user.EmailConfirmed,
                user.SecurityStamp
            })
            .FirstOrDefaultAsync();

        if (userState is null ||
            !userState.IsActive ||
            !string.Equals(userState.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
        {
            return;
        }

        if (requirement.RequireConfirmedEmail && !userState.EmailConfirmed)
        {
            return;
        }

        context.Succeed(requirement);
    }
}
