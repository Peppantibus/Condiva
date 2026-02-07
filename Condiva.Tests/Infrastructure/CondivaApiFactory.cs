using System.Linq;
using Condiva.Api.Features.Notifications.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

        // WebApplicationFactory does not reliably override configuration early enough with minimal hosting.
        // Set environment variables so WebApplication.CreateBuilder picks them up.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "DataSource=:memory:");
        Environment.SetEnvironmentVariable("JwtSettings__Key", "test-test-test-test-test-test-test-test");
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", "Condiva-Tests");
        Environment.SetEnvironmentVariable("JwtSettings__Audience", "Condiva-Tests");
        Environment.SetEnvironmentVariable("JwtSettings__AccessTokenLifetimeMinutes", "15");
        Environment.SetEnvironmentVariable("SecuritySettings__Pepper", "test-pepper");
        Environment.SetEnvironmentVariable("MailService__DisableSend", "true");
        Environment.SetEnvironmentVariable("AuthSettings__AutoVerifyEmail", "true");
        Environment.SetEnvironmentVariable("TemplateSettings__BasePath", "templates");
        Environment.SetEnvironmentVariable("RefreshTokenSettings__RefreshTokenLifetimeDays", "30");
        Environment.SetEnvironmentVariable("NotificationProcessing__Enabled", "false");
        Environment.SetEnvironmentVariable("NotificationProcessing__PollIntervalSeconds", "1");
        Environment.SetEnvironmentVariable("NotificationProcessing__BatchSize", "50");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddDebug();
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
