using AuthLibrary.Models;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Notifications.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Reputations.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;

public class CondivaDbContext : DbContext
{
    public CondivaDbContext(DbContextOptions<CondivaDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalAuthLogin> ExternalAuthLogins => Set<ExternalAuthLogin>();
    public DbSet<EmailVerifiedToken> EmailVerifiedTokens => Set<EmailVerifiedToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRule> NotificationRuleMappings => Set<NotificationRule>();
    public DbSet<NotificationDispatchState> NotificationDispatchStates => Set<NotificationDispatchState>();
    public DbSet<ReputationProfile> Reputations => Set<ReputationProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!Database.IsSqlite())
        {
            modelBuilder.HasDefaultSchema("public");
        }
        modelBuilder.Entity<User>().HasKey(user => user.Id);
        modelBuilder.Entity<User>().HasIndex(user => user.Username).IsUnique();
        modelBuilder.Entity<User>().HasIndex(user => user.Email).IsUnique();
        modelBuilder.Entity<ExternalAuthLogin>()
            .HasKey(login => new { login.Provider, login.Subject });
        modelBuilder.Entity<EmailVerifiedToken>().HasKey(token => token.TokenHash);
        modelBuilder.Entity<PasswordResetToken>().HasKey(token => token.TokenHash);
        modelBuilder.Entity<RefreshToken>().HasKey(token => token.TokenHash);
        modelBuilder.Entity<Community>().HasKey(community => community.Id);
        modelBuilder.Entity<Membership>().HasKey(membership => membership.Id);
        modelBuilder.Entity<Item>().HasKey(item => item.Id);
        modelBuilder.Entity<Request>().HasKey(request => request.Id);
        modelBuilder.Entity<Offer>().HasKey(offer => offer.Id);
        modelBuilder.Entity<Loan>().HasKey(loan => loan.Id);
        modelBuilder.Entity<Event>().HasKey(evt => evt.Id);
        modelBuilder.Entity<Notification>().HasKey(notification => notification.Id);
        modelBuilder.Entity<NotificationRule>()
            .HasKey(rule => new { rule.EntityType, rule.Action, rule.Type });
        modelBuilder.Entity<Notification>()
            .HasIndex(notification => new
            {
                notification.EventId,
                notification.Type,
                notification.RecipientUserId
            })
            .IsUnique();
        modelBuilder.Entity<NotificationDispatchState>().HasKey(state => state.Id);
        modelBuilder.Entity<ReputationProfile>().HasKey(reputation => reputation.Id);

        modelBuilder.Entity<User>()
            .HasMany(user => user.EmailVerifiedTokens)
            .WithOne()
            .HasForeignKey(token => token.UserId);

        modelBuilder.Entity<User>()
            .HasMany(user => user.PasswordResetTokens)
            .WithOne()
            .HasForeignKey(token => token.UserId);

        modelBuilder.Entity<User>()
            .HasMany(user => user.RefreshTokens)
            .WithOne()
            .HasForeignKey(token => token.UserId);

        modelBuilder.Entity<ExternalAuthLogin>()
            .HasIndex(login => login.UserId);
        modelBuilder.Entity<ExternalAuthLogin>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(login => login.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
