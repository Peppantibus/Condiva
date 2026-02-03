using System.Collections.Generic;
using System.Linq;
using Condiva.Api.Features.Notifications.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Condiva.Tests.Infrastructure;

public sealed class CondivaApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public CondivaApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddDebug();
        });

        builder.ConfigureAppConfiguration(config =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
                ["JwtSettings:Key"] = "test-test-test-test-test-test-test-test",
                ["JwtSettings:Issuer"] = "Condiva-Tests",
                ["JwtSettings:Audience"] = "Condiva-Tests",
                ["JwtSettings:AccessTokenLifetimeMinutes"] = "15",
                ["SecuritySettings:Pepper"] = "test-pepper",
                ["MailService:DisableSend"] = "true",
                ["AuthSettings:AutoVerifyEmail"] = "true",
                ["TemplateSettings:BasePath"] = "templates",
                ["RefreshTokenSettings:RefreshTokenLifetimeDays"] = "30",
                ["Redis:Url"] = "",
                ["NotificationProcessing:Enabled"] = "false",
                ["NotificationProcessing:PollIntervalSeconds"] = "1",
                ["NotificationProcessing:BatchSize"] = "50"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<CondivaDbContext>));
            services.AddDbContext<CondivaDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
            dbContext.Database.EnsureCreated();
            SeedNotificationRules(dbContext);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private static void SeedNotificationRules(CondivaDbContext dbContext)
    {
        if (dbContext.NotificationRuleMappings.Any())
        {
            return;
        }

        dbContext.NotificationRuleMappings.AddRange(new[]
        {
            new NotificationRule
            {
                EntityType = "Offer",
                Action = "OfferCreated",
                Type = NotificationType.OfferReceivedToRequester
            },
            new NotificationRule
            {
                EntityType = "Loan",
                Action = "LoanReturned",
                Type = NotificationType.LoanReturnConfirmedToBorrower
            },
            new NotificationRule
            {
                EntityType = "Loan",
                Action = "LoanReturned",
                Type = NotificationType.LoanReturnConfirmedToLender
            }
        });

        dbContext.SaveChanges();
    }
}
