using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using Condiva.Api.Features.Reputations.Models;

namespace Condiva.Api.Common.Auth.Models;

public class User : IAuthUser
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public DateTime? PasswordUpdatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfileImageKey { get; set; }

    public ICollection<EmailVerifiedToken> EmailVerifiedTokens { get; set; } =
        new List<EmailVerifiedToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } =
        new List<PasswordResetToken>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } =
        new List<RefreshToken>();
    public ICollection<ReputationProfile> Reputations { get; set; } =
        new List<ReputationProfile>();
}
