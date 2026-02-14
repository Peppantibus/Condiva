using AuthLibrary.Interfaces;
using AuthLibrary.Models.Dto.Auth;
using Condiva.Api.Common.Auth.Models;

namespace Condiva.Api.Common.Auth.Services;

public sealed class ExternalUserFactory : IExternalUserFactory<User>
{
    public User CreateFromExternal(ExternalUserInfo info)
    {
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = string.Empty,
            Email = string.Empty,
            Password = string.Empty,
            Salt = string.Empty,
            EmailVerified = info.EmailVerified,
            PasswordUpdatedAt = DateTime.UtcNow,
            Name = Normalize(info.GivenName) ?? Normalize(info.Name) ?? string.Empty,
            LastName = Normalize(info.FamilyName) ?? string.Empty
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
