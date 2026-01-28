using System.Security.Claims;

namespace Condiva.Api.Common.Auth;

public interface ICurrentUser
{
    string? GetUserId(ClaimsPrincipal user);
}

public sealed class CurrentUser : ICurrentUser
{
    public string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("userId")?.Value
            ?? user.FindFirst("id")?.Value;
    }
}
