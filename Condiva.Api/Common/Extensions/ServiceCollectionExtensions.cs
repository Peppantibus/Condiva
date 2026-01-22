using System;
using AuthLibrary.Extensions;
using AuthLibrary.Interfaces;
using Condiva.Api.Common.Auth.Data;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Auth.Services;
using Condiva.Api.Features.Communities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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


        services.AddControllers();
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
        services.AddAuthLibrary<User>(configuration);
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

        services.AddCommunities(configuration);

        return services;
    }
}
