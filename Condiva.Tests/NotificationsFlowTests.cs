using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Notifications.Dtos;
using Condiva.Api.Features.Notifications.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Features.Notifications.Services;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Condiva.Tests;

public sealed class NotificationsFlowTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public NotificationsFlowTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OfferCreated_GeneratesNotificationForRequester()
    {
        var requesterId = "notif-requester";
        var lenderId = "notif-lender";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsJsonAsync("/api/offers", new
        {
            CommunityId = communityId,
            OffererUserId = lenderId,
            RequestId = requestId,
            ItemId = itemId,
            Message = "Offer",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await ProcessNotificationsAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
            var notification = await dbContext.Notifications.FirstOrDefaultAsync(notification =>
                notification.RecipientUserId == requesterId
                && notification.Type == NotificationType.OfferReceivedToRequester);
            Assert.NotNull(notification);
            Assert.Equal("Offer", notification!.EntityType);
        }
    }

    [Fact]
    public async Task LoanReturned_GeneratesNotificationsForBorrowerAndLender()
    {
        var borrowerId = "notif-borrower";
        var lenderId = "notif-lender-return";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId, ItemStatus.InLoan);
        var loanId = await SeedLoanAsync(communityId, itemId, lenderId, borrowerId, LoanStatus.ReturnRequested);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsync($"/api/loans/{loanId}/return-confirm", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ProcessNotificationsAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
            var notifications = await dbContext.Notifications
                .Where(notification => notification.EventId != null)
                .ToListAsync();
            Assert.Contains(notifications, notification =>
                notification.RecipientUserId == borrowerId
                && notification.Type == NotificationType.LoanReturnConfirmedToBorrower);
            Assert.Contains(notifications, notification =>
                notification.RecipientUserId == lenderId
                && notification.Type == NotificationType.LoanReturnConfirmedToLender);
        }
    }

    [Fact]
    public async Task GetNotifications_WithFilters_ReturnsPagedUnread()
    {
        var userId = "notif-reader";
        var otherId = "notif-other";
        var communityId = await SeedCommunityWithMembersAsync(userId, otherId);
        await SeedNotificationAsync(userId, communityId, NotificationType.LoanReservedToBorrower, read: false);
        await SeedNotificationAsync(userId, communityId, NotificationType.LoanStartedToBorrower, read: true);
        await SeedNotificationAsync(userId, "other-community", NotificationType.LoanStartedToBorrower, read: false);

        using var client = CreateClientWithToken(userId);
        var response = await client.GetAsync(
            $"/api/notifications?communityId={communityId}&unreadOnly=true&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Condiva.Api.Common.Results.PagedResult<NotificationListItemDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(NotificationType.LoanReservedToBorrower, payload.Items[0].Type);
    }

    [Fact]
    public async Task BulkMarkRead_MarksNotifications()
    {
        var userId = "notif-bulk";
        var communityId = await SeedCommunityWithMembersAsync(userId);
        var firstId = await SeedNotificationAsync(userId, communityId, NotificationType.LoanReservedToBorrower, read: false);
        var secondId = await SeedNotificationAsync(userId, communityId, NotificationType.LoanStartedToBorrower, read: false);

        using var client = CreateClientWithToken(userId);
        var response = await client.PostAsJsonAsync("/api/notifications/read", new NotificationMarkReadRequestDto(
            new List<string> { firstId, secondId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var notifications = await dbContext.Notifications
            .Where(notification => notification.Id == firstId || notification.Id == secondId)
            .ToListAsync();
        Assert.All(notifications, notification => Assert.NotNull(notification.ReadAt));
    }

    private HttpClient CreateClientWithToken(string userId)
    {
        var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        var token = CreateJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> SeedCommunityWithMembersAsync(string ownerId, string? memberId = null)
    {
        await SeedUserAsync(ownerId);
        if (!string.IsNullOrWhiteSpace(memberId))
        {
            await SeedUserAsync(memberId);
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var communityId = Guid.NewGuid().ToString();
        dbContext.Communities.Add(new Community
        {
            Id = communityId,
            Name = "Test Community",
            Slug = $"test-{communityId}",
            CreatedByUserId = ownerId,
            EnterCode = Guid.NewGuid().ToString("N"),
            EnterCodeExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });

        dbContext.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid().ToString(),
            UserId = ownerId,
            CommunityId = communityId,
            Role = MembershipRole.Owner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(memberId))
        {
            dbContext.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid().ToString(),
                UserId = memberId,
                CommunityId = communityId,
                Role = MembershipRole.Member,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                JoinedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
        return communityId;
    }

    private async Task SeedUserAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var existing = await dbContext.Users.FindAsync(userId);
        if (existing is not null)
        {
            return;
        }

        dbContext.Users.Add(new User
        {
            Id = userId,
            Username = $"{userId}-name",
            Email = $"{userId}@example.com",
            Password = "hashed-password",
            Salt = "salt",
            EmailVerified = true,
            Name = "Test",
            LastName = "User"
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<string> SeedItemAsync(string communityId, string ownerId, ItemStatus status = ItemStatus.Available)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var item = new Item
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            OwnerUserId = ownerId,
            Name = "Test item",
            Description = "Desc",
            Category = "Tools",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Items.Add(item);
        await dbContext.SaveChangesAsync();
        return item.Id;
    }

    private async Task<string> SeedRequestAsync(string communityId, string requesterId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var request = new Request
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync();
        return request.Id;
    }

    private async Task<string> SeedLoanAsync(
        string communityId,
        string itemId,
        string lenderUserId,
        string borrowerUserId,
        LoanStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var loan = new Condiva.Api.Features.Loans.Models.Loan
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = lenderUserId,
            BorrowerUserId = borrowerUserId,
            Status = status,
            StartAt = DateTime.UtcNow,
            ReturnRequestedAt = DateTime.UtcNow
        };
        dbContext.Loans.Add(loan);
        await dbContext.SaveChangesAsync();
        return loan.Id;
    }

    private async Task<string> SeedNotificationAsync(
        string recipientUserId,
        string communityId,
        NotificationType type,
        bool read)
    {
        await EnsureCommunityAsync(communityId, recipientUserId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var notification = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            RecipientUserId = recipientUserId,
            CommunityId = communityId,
            Type = type,
            Status = read ? NotificationStatus.Delivered : NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ReadAt = read ? DateTime.UtcNow : null
        };
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();
        return notification.Id;
    }
    private async Task EnsureCommunityAsync(string communityId, string ownerUserId)
    {
        await SeedUserAsync(ownerUserId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var existing = await dbContext.Communities.FindAsync(communityId);
        if (existing is not null)
        {
            return;
        }

        dbContext.Communities.Add(new Community
        {
            Id = communityId,
            Name = "Test Community",
            Slug = $"test-{communityId}",
            CreatedByUserId = ownerUserId,
            EnterCode = Guid.NewGuid().ToString("N"),
            EnterCodeExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task ProcessNotificationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<INotificationsProcessor>();
        await processor.ProcessBatchAsync(default);
    }

    private string CreateJwt(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var signingKey = options.TokenValidationParameters.IssuerSigningKey
            ?? throw new InvalidOperationException("Jwt signing key missing.");
        var issuer = options.TokenValidationParameters.ValidIssuer;
        var audience = options.TokenValidationParameters.ValidAudience;
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
