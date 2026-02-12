using System;
using System.Text;
using System.Threading.RateLimiting;
using AuthLibrary.Extensions;
using AuthLibrary.Interfaces;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Condiva.Api.Common.Auth.Configuration;
using Condiva.Api.Common.Auth.Data;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Auth.Services;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities;
using Condiva.Api.Features.Communities.Data;
using Condiva.Api.Features.Events.Data;
using Condiva.Api.Features.Items.Data;
using Condiva.Api.Features.Loans.Data;
using Condiva.Api.Features.Memberships.Data;
using Condiva.Api.Features.Notifications.Models;
using Condiva.Api.Features.Notifications.Data;
using Condiva.Api.Features.Notifications.Services;
using Condiva.Api.Features.Offers.Data;
using Condiva.Api.Features.Reputations.Data;
using Condiva.Api.Features.Requests.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace Condiva.Api.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCondivaServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Default' is not configured.");
        }

        services.AddDbContext<CondivaDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });


        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", document, null),
                    new List<string>()
                }
            });
        });

        services.AddHttpContextAccessor();
        services.Configure<AuthCookieSettings>(configuration.GetSection("AuthCookies"));
        services.AddAuthLibrary<User>(configuration);

        // AuthLibrary.Core 1.0.5 registers AuthService<TUser> with multiple valid constructors.
        // Built-in DI cannot choose and throws "constructors are ambiguous". We force the intended constructor here.
        services.RemoveAll(typeof(IAuthService<User>));
        services.AddScoped<IAuthService<User>>(sp =>
            new AuthLibrary.Services.AuthService<User>(
                sp.GetRequiredService<ILoginService<User>>(),
                sp.GetRequiredService<IExternalLoginService<User>>(),
                sp.GetRequiredService<IRegisterService<User>>(),
                sp.GetRequiredService<IEmailVerificationService<User>>(),
                sp.GetRequiredService<IPasswordFlowService<User>>()));

        // ASP.NET rate limiter is required because /api/auth endpoints use RequireRateLimiting("auth").
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("auth", limiter =>
            {
                limiter.PermitLimit = configuration.GetValue<int?>("RateLimit:AuthEndpoint:PermitLimit") ?? 30;
                limiter.Window = TimeSpan.FromSeconds(
                    Math.Max(configuration.GetValue<int?>("RateLimit:AuthEndpoint:WindowSeconds") ?? 60, 1));
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });
        var jwtSettings = configuration.GetSection("JwtSettings");
        var jwtKey = jwtSettings.GetValue<string>("Key");
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException("JwtSettings:Key is not configured.");
        }
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = jwtSettings.GetValue<string>("Issuer"),
                ValidAudience = jwtSettings.GetValue<string>("Audience"),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });
        if (configuration.GetValue<bool>("MailService:DisableSend"))
        {
            services.AddScoped<IMailService, NoopMailService>();
        }
        services.AddScoped<IAuthRepository<User>, AuthRepository>();
        services.AddScoped<ITransactionalAuthRepository<User>, AuthRepository>();

        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        if (corsOrigins.Length == 0)
        {
            var frontendUrl = configuration.GetValue<string>("AuthSettings:FrontendUrl");
            if (!string.IsNullOrWhiteSpace(frontendUrl))
            {
                corsOrigins = new[] { frontendUrl.Trim() };
            }
        }

        if (corsOrigins.Length > 0)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                {
                    policy.WithOrigins(corsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }

        services.AddScoped<ICommunityRepository, CommunityRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IReputationRepository, ReputationRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<NotificationRules>();
        services.AddSingleton<INotificationsProcessor, NotificationsProcessor>();
        services.AddHostedService<NotificationsBackgroundService>();

        var mapperRegistry = new MapperRegistry();
        MappingRegistration.RegisterAll(mapperRegistry);
        services.AddSingleton(mapperRegistry);
        services.AddSingleton<IMapper, AppMapper>();

        return services;
    }
}
