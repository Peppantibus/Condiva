using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using Condiva.Api.Common.Auth.Models;
using Microsoft.EntityFrameworkCore;

namespace Condiva.Api.Common.Auth.Data;

public sealed class AuthRepository : IAuthRepository<User>
{
    private readonly CondivaDbContext _dbContext;

    public AuthRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
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

    public Task SaveChangesAsync()
    {
        return _dbContext.SaveChangesAsync();
    }
}
