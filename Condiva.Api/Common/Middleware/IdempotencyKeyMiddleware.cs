using System.Security.Cryptography;
using System.Text;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Idempotency;
using Condiva.Api.Common.Idempotency.Configuration;
using Condiva.Api.Common.Idempotency.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Condiva.Api.Common.Middleware;

public sealed class IdempotencyKeyMiddleware
{
    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/items",
        "/api/requests",
        "/api/offers",
        "/api/loans",
        "/api/communities/join"
    };

    private const string CommunitiesPrefix = "/api/communities/";
    private const string InviteRotateSuffix = "/invite-code/rotate";

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdempotencySettings _settings;

    public IdempotencyKeyMiddleware(
        RequestDelegate next,
        IServiceScopeFactory scopeFactory,
        IOptions<IdempotencySettings> options)
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _settings = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldHandleRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var key = Normalize(context.Request.Headers[IdempotencyHeaders.Key].ToString());
        if (key is null)
        {
            await _next(context);
            return;
        }

        var validationError = ValidateKey(key);
        if (validationError is not null)
        {
            await validationError.ExecuteAsync(context);
            return;
        }

        var actorUserId = CurrentUser.GetUserId(context.User) ?? "anonymous";
        var method = context.Request.Method.ToUpperInvariant();
        var path = NormalizePath(context.Request.Path);
        var requestHash = await ComputeRequestHashAsync(context.Request);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(Math.Max(_settings.ReplayTtlHours, 1));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        await RemoveExpiredRecordsAsync(dbContext, now, context.RequestAborted);

        var existing = await FindRecordAsync(
            dbContext,
            actorUserId,
            method,
            path,
            key,
            context.RequestAborted);
        if (existing is not null)
        {
            if (await TryHandleExistingRecordAsync(context, existing, requestHash))
            {
                return;
            }
        }

        var pendingRecord = new IdempotencyRecord
        {
            ActorUserId = actorUserId,
            Method = method,
            Path = path,
            IdempotencyKey = key,
            RequestHash = requestHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt
        };

        dbContext.IdempotencyRecords.Add(pendingRecord);
        try
        {
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(pendingRecord).State = EntityState.Detached;
            existing = await FindRecordAsync(
                dbContext,
                actorUserId,
                method,
                path,
                key,
                context.RequestAborted);
            if (existing is not null
                && await TryHandleExistingRecordAsync(context, existing, requestHash))
            {
                return;
            }

            throw;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            dbContext.IdempotencyRecords.Remove(pendingRecord);
            await dbContext.SaveChangesAsync(context.RequestAborted);
            throw;
        }

        var capturedBody = await ReadBufferAsync(buffer);
        try
        {
            if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                dbContext.IdempotencyRecords.Remove(pendingRecord);
            }
            else
            {
                pendingRecord.ResponseStatusCode = context.Response.StatusCode;
                pendingRecord.ResponseBody = capturedBody;
                pendingRecord.ResponseContentType = context.Response.ContentType;
                pendingRecord.ResponseLocation = context.Response.Headers.Location.ToString();
                pendingRecord.CompletedAtUtc = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(context.RequestAborted);
            context.Response.Headers[IdempotencyHeaders.Replayed] = "false";
            await WriteBufferToResponseAsync(context, buffer, originalBody, context.RequestAborted);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }
    }

    private static bool ShouldHandleRequest(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && IsProtectedPath(request.Path);
    }

    private static bool IsProtectedPath(PathString path)
    {
        var normalizedPath = NormalizePath(path);
        if (ProtectedPaths.Contains(normalizedPath))
        {
            return true;
        }

        if (!normalizedPath.StartsWith(CommunitiesPrefix, StringComparison.OrdinalIgnoreCase)
            || !normalizedPath.EndsWith(InviteRotateSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idSegmentLength = normalizedPath.Length - CommunitiesPrefix.Length - InviteRotateSuffix.Length;
        if (idSegmentLength <= 0)
        {
            return false;
        }

        var idSegment = normalizedPath.Substring(CommunitiesPrefix.Length, idSegmentLength);
        return !idSegment.Contains('/');
    }

    private IResult? ValidateKey(string key)
    {
        var minLength = Math.Max(_settings.MinKeyLength, 1);
        var maxLength = Math.Max(_settings.MaxKeyLength, minLength);
        if (key.Length < minLength || key.Length > maxLength)
        {
            return ApiErrors.Validation(
                $"Idempotency-Key length must be between {minLength} and {maxLength} characters.");
        }

        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
        {
            return ApiErrors.Validation("Idempotency-Key contains invalid characters.");
        }

        return null;
    }

    private static async Task<IdempotencyRecord?> FindRecordAsync(
        CondivaDbContext dbContext,
        string actorUserId,
        string method,
        string path,
        string key,
        CancellationToken cancellationToken)
    {
        return await dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(record =>
                    record.ActorUserId == actorUserId
                    && record.Method == method
                    && record.Path == path
                    && record.IdempotencyKey == key,
                cancellationToken);
    }

    private static async Task<bool> TryHandleExistingRecordAsync(
        HttpContext context,
        IdempotencyRecord existing,
        string requestHash)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            await ApiErrors.Conflict(
                    "Idempotency-Key is already used with a different payload.")
                .ExecuteAsync(context);
            return true;
        }

        if (existing.ResponseStatusCode is null)
        {
            await ApiErrors.Conflict(
                    "A request with this Idempotency-Key is already in progress.")
                .ExecuteAsync(context);
            return true;
        }

        context.Response.StatusCode = existing.ResponseStatusCode.Value;
        if (!string.IsNullOrWhiteSpace(existing.ResponseContentType))
        {
            context.Response.ContentType = existing.ResponseContentType;
        }
        if (!string.IsNullOrWhiteSpace(existing.ResponseLocation))
        {
            context.Response.Headers.Location = existing.ResponseLocation;
        }

        context.Response.Headers[IdempotencyHeaders.Replayed] = "true";
        if (!string.IsNullOrWhiteSpace(existing.ResponseBody))
        {
            await context.Response.WriteAsync(existing.ResponseBody);
        }

        return true;
    }

    private static async Task RemoveExpiredRecordsAsync(
        CondivaDbContext dbContext,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await dbContext.IdempotencyRecords
            .Where(record => record.ExpiresAtUtc <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<string> ReadBufferAsync(MemoryStream buffer)
    {
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteBufferToResponseAsync(
        HttpContext context,
        MemoryStream buffer,
        Stream originalBody,
        CancellationToken cancellationToken)
    {
        buffer.Position = 0;
        context.Response.Body = originalBody;
        await buffer.CopyToAsync(originalBody, cancellationToken);
    }

    private static string NormalizePath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 1)
        {
            trimmed = trimmed.TrimEnd('/');
        }

        return trimmed;
    }

    private static async Task<string> ComputeRequestHashAsync(HttpRequest request)
    {
        request.EnableBuffering();

        await using var bodyBuffer = new MemoryStream();
        await request.Body.CopyToAsync(bodyBuffer, request.HttpContext.RequestAborted);
        request.Body.Position = 0;

        var contentTypeBytes = Encoding.UTF8.GetBytes(request.ContentType ?? string.Empty);
        var bodyBytes = bodyBuffer.ToArray();
        var payload = new byte[contentTypeBytes.Length + bodyBytes.Length + 1];
        Buffer.BlockCopy(contentTypeBytes, 0, payload, 0, contentTypeBytes.Length);
        payload[contentTypeBytes.Length] = 0;
        Buffer.BlockCopy(bodyBytes, 0, payload, contentTypeBytes.Length + 1, bodyBytes.Length);

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public static class IdempotencyKeyExtensions
{
    public static IApplicationBuilder UseIdempotencyKey(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyKeyMiddleware>();
    }
}
