using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using Condiva.Api.Common.Auth.Models;
using Microsoft.EntityFrameworkCore;

namespace Condiva.Api.Common.Auth.Data;

public sealed class AuthRepository : IAuthRepository<User>, ITransactionalAuthRepository<User>
{
    private readonly CondivaDbContext _dbContext;

    public AuthRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            await operation();
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        await operation();
        await transaction.CommitAsync();
    }

    public Task<User?> GetUserByUsernameAsync(string username)
    {
        return _dbContext.Users.FirstOrDefaultAsync(user => user.Username == username);
    }

    public Task<User?> GetUserByEmailAsync(string email)
    {
        return _dbContext.Users.FirstOrDefaultAsync(user => user.Email == email);
    }

    public Task<User?> GetUserByIdAsync(string id)
    {
        return _dbContext.Users.FirstOrDefaultAsync(user => user.Id == id);
    }

    public Task<bool> UserExistsAsync(string username, string email)
    {
        return _dbContext.Users.AnyAsync(user => user.Username == username || user.Email == email);
    }

    public Task AddUserAsync(User user)
    {
        return _dbContext.Users.AddAsync(user).AsTask();
    }

    public Task UpdateUserAsync(User user)
    {
        _dbContext.Users.Update(user);
        return Task.CompletedTask;
    }

    public Task RemoveUserAsync(User user)
    {
        _dbContext.Users.Remove(user);
        return Task.CompletedTask;
    }

    public Task AddEmailVerifiedTokenAsync(EmailVerifiedToken token)
    {
        return _dbContext.EmailVerifiedTokens.AddAsync(token).AsTask();
    }

    public Task<EmailVerifiedToken?> GetEmailVerifiedTokenAsync(string tokenHash)
    {
        return _dbContext.EmailVerifiedTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash);
    }

    public Task RemoveEmailVerifiedTokenAsync(EmailVerifiedToken token)
    {
        _dbContext.EmailVerifiedTokens.Remove(token);
        return Task.CompletedTask;
    }

    public Task RemoveEmailVerifiedTokensByUserIdAsync(string userId)
    {
        var tokens = _dbContext.EmailVerifiedTokens.Where(token => token.UserId == userId);
        _dbContext.EmailVerifiedTokens.RemoveRange(tokens);
        return Task.CompletedTask;
    }

    public Task AddPasswordResetTokenAsync(PasswordResetToken token)
    {
        return _dbContext.PasswordResetTokens.AddAsync(token).AsTask();
    }

    public Task<PasswordResetToken?> GetPasswordResetTokenAsync(string tokenHash)
    {
        return _dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash);
    }

    public Task RemovePasswordResetTokenAsync(PasswordResetToken token)
    {
        _dbContext.PasswordResetTokens.Remove(token);
        return Task.CompletedTask;
    }

    public Task RemovePasswordResetTokensByUserIdAsync(string userId)
    {
        var tokens = _dbContext.PasswordResetTokens.Where(token => token.UserId == userId);
        _dbContext.PasswordResetTokens.RemoveRange(tokens);
        return Task.CompletedTask;
    }

    public Task AddRefreshTokenAsync(RefreshToken token)
    {
        return _dbContext.RefreshTokens.AddAsync(token).AsTask();
    }

    public Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        return _dbContext.RefreshTokens
            .FirstOrDefaultAsync(refreshToken => refreshToken.TokenHash == token);
    }

    public Task UpdateRefreshTokenAsync(RefreshToken token)
    {
        _dbContext.RefreshTokens.Update(token);
        return Task.CompletedTask;
    }

    public Task RemoveRefreshTokensByUserIdAsync(string userId)
    {
        var tokens = _dbContext.RefreshTokens.Where(token => token.UserId == userId);
        _dbContext.RefreshTokens.RemoveRange(tokens);
        return Task.CompletedTask;
    }

    public async Task<bool> TryRotateRefreshTokenAsync(
        string oldTokenHash,
        string newTokenHash,
        DateTime revokedAt,
        DateTime newTokenCreatedAt,
        DateTime newTokenExpiresAt)
    {
        var alreadyInTransaction = _dbContext.Database.CurrentTransaction is not null;
        await using var transaction = alreadyInTransaction
            ? null
            : await _dbContext.Database.BeginTransactionAsync();

        var userId = await _dbContext.RefreshTokens
            .Where(token => token.TokenHash == oldTokenHash)
            .Select(token => token.UserId)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        // Atomic rotation under concurrency:
        // only one caller can revoke+replace an active token (revokedAt == null && replacedByToken == null).
        var updated = await _dbContext.RefreshTokens
            .Where(token =>
                token.TokenHash == oldTokenHash
                && token.RevokedAt == null
                && token.ReplacedByToken == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.ReplacedByToken, newTokenHash));

        if (updated == 0)
        {
            return false;
        }

        await _dbContext.RefreshTokens.AddAsync(new RefreshToken
        {
            TokenHash = newTokenHash,
            UserId = userId,
            CreatedAt = newTokenCreatedAt,
            ExpiresAt = newTokenExpiresAt,
            RevokedAt = null,
            ReplacedByToken = null
        });

        await _dbContext.SaveChangesAsync();
        if (!alreadyInTransaction)
        {
            await transaction!.CommitAsync();
        }

        return true;
    }

    public Task<ExternalAuthLogin?> GetExternalLoginAsync(string provider, string subject)
    {
        return _dbContext.ExternalAuthLogins
            .FirstOrDefaultAsync(login => login.Provider == provider && login.Subject == subject);
    }

    public Task AddExternalLoginAsync(ExternalAuthLogin login)
    {
        return _dbContext.ExternalAuthLogins.AddAsync(login).AsTask();
    }

    public Task SaveChangesAsync()
    {
        return _dbContext.SaveChangesAsync();
    }
}
