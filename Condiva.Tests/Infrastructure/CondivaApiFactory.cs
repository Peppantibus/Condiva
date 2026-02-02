using System.Collections.Generic;
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
                ["NotificationProcessing:BatchSize"] = "50",
                ["NotificationRules:Mappings:0:EntityType"] = "Offer",
                ["NotificationRules:Mappings:0:Action"] = "OfferCreated",
                ["NotificationRules:Mappings:0:Types:0"] = "OfferReceivedToRequester",
                ["NotificationRules:Mappings:1:EntityType"] = "Loan",
                ["NotificationRules:Mappings:1:Action"] = "LoanReturned",
                ["NotificationRules:Mappings:1:Types:0"] = "LoanReturnConfirmedToBorrower",
                ["NotificationRules:Mappings:1:Types:1"] = "LoanReturnConfirmedToLender"
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
}
