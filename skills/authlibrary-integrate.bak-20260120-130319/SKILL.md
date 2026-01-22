---
name: authlibrary-integrate
description: "Integrate AuthLibrary.Core into ASP.NET Core projects (Minimal API or MVC) with appsettings, DI registration, repository implementation, templates, and endpoints. Use when adding the AuthLibrary auth flow (login, refresh, email verify, reset) to a new project."
---

# AuthLibrary integration

## Inputs to confirm

- Project type: Minimal API or MVC
- User entity type (e.g. MyUser)
- Storage tech for IAuthRepository (e.g. EF Core DbContext)
- Template folder path (default: templates)
- Whether trusted proxies are needed for rate limiting

## Workflow

1) Add NuGet package

```bash

dotnet add package AuthLibrary.Core --version 1.0.0-alpha.4
```

2) Add configuration to appsettings.json

```json
{
  "JwtSettings": {
    "Key": "this-is-a-very-long-secret-key-at-least-32-bytes",
    "Issuer": "MyIssuer",
    "Audience": "MyAudience",
    "AccessTokenLifetimeMinutes": 15
  },
  "SecuritySettings": {
    "Pepper": "my-app-secret-pepper"
  },
  "MailService": {
    "AppMail": "noreply@example.com",
    "Host": "smtp.example.com",
    "Port": 587,
    "SenderName": "My App",
    "Username": "smtp-user",
    "Password": "smtp-pass",
    "UseSsl": true
  },
  "AuthSettings": {
    "FrontendUrl": "https://app.example.com"
  },
  "TemplateSettings": {
    "BasePath": "templates"
  },
  "RefreshTokenSettings": {
    "RefreshTokenLifetimeDays": 30
  },
  "RateLimit": {
    "Rules": {
      "Login": {
        "MaxUserAttempts": 5,
        "MaxIpAttempts": 20,
        "AttemptWindow": "00:15:00",
        "LockDuration": "00:05:00"
      },
      "Register": {
        "MaxUserAttempts": 3,
        "MaxIpAttempts": 10,
        "AttemptWindow": "00:30:00",
        "LockDuration": "00:10:00"
      },
      "VerifyEmail": {
        "MaxUserAttempts": 5,
        "MaxIpAttempts": 15,
        "AttemptWindow": "01:00:00",
        "LockDuration": "00:15:00"
      },
      "ResetPassword": {
        "MaxUserAttempts": 3,
        "MaxIpAttempts": 10,
        "AttemptWindow": "00:30:00",
        "LockDuration": "00:15:00"
      }
    }
  },
  "Redis": {
    "Url": "localhost:6379"
  }
}
```

Notes:
- JwtSettings: Key must be at least 32 bytes
- SecuritySettings: Pepper is required
- Redis is optional; library falls back to in-memory

3) Add templates

- `templates/VerifyEmail.html`
- `templates/ResetPassword.html`

Use placeholders `{{username}}` and `{{url}}`.

4) Register services

Minimal API (Program.cs):

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthLibrary<MyUser>(builder.Configuration);

// Register repository implementation
builder.Services.AddScoped<IAuthRepository<MyUser>, AuthRepository>();
```

MVC (Program.cs or Startup.cs):

```csharp
services.AddHttpContextAccessor();
services.AddAuthLibrary<MyUser>(Configuration);
services.AddScoped<IAuthRepository<MyUser>, AuthRepository>();
```

Optional trusted proxies for rate limiting:

```csharp
services.AddScoped<IRateLimitService>(sp =>
{
    var redis = sp.GetRequiredService<IRedisService>();
    var http = sp.GetRequiredService<IHttpContextAccessor>();
    return new RateLimitService(
        redis,
        http,
        trustedProxyIps: new[] { "10.0.0.1", "10.0.0.2" });
});
```

5) Implement IAuthRepository

- Implement `IAuthRepository<TUser>` for your storage backend
- Include methods for users, email tokens, reset tokens, refresh tokens
- Register the implementation in DI (step 4)

6) Add auth endpoints

Example MVC controller endpoints:

```csharp
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService<MyUser> _auth;
    private readonly ITokenService<MyUser> _token;

    public AuthController(IAuthService<MyUser> auth, ITokenService<MyUser> token)
    {
        _auth = auth;
        _token = token;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto body)
    {
        var result = await _auth.Login(body.Username, body.Password);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] string refreshToken)
    {
        var result = await _token.TryRefreshToken(refreshToken);
        return result.IsSuccess ? Ok(result.Value) : Unauthorized(result.Error);
    }
}
```

Minimal API example:

```csharp
app.MapPost("/api/auth/login", async (LoginDto body, IAuthService<MyUser> auth) =>
{
    var result = await auth.Login(body.Username, body.Password);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.MapPost("/api/auth/refresh", async (string refreshToken, ITokenService<MyUser> token) =>
{
    var result = await token.TryRefreshToken(refreshToken);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Unauthorized();
});
```

## Checklist

- Package installed
- appsettings.json updated
- templates added
- services registered
- IAuthRepository implemented and registered
- auth endpoints wired
