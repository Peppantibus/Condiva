using System.Security.Claims;

namespace Condiva.Api.Common.Auth;

public static class CurrentUser
{
    public static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("userId")?.Value
            ?? user.FindFirst("id")?.Value;
    }
}
