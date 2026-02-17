using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Condiva.Api.Common.Errors;

public static class ApiErrors
{
    public static IResult Required(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return Validation("Invalid input.");
        }

        return Validation("Invalid input.", new Dictionary<string, string>
        {
            [field] = $"{field} is required."
        });
    }

    public static IResult Invalid(string message)
    {
        return Build(
            StatusCodes.Status400BadRequest,
            "validation_error",
            string.IsNullOrWhiteSpace(message) ? "Invalid input." : message);
    }

    public static IResult NotFound(string entity)
    {
        var message = string.IsNullOrWhiteSpace(entity)
            ? "Resource not found."
            : $"{entity} not found.";
        return Build(StatusCodes.Status404NotFound, "not_found", message);
    }

    public static IResult Unauthorized()
    {
        return Build(StatusCodes.Status401Unauthorized, "unauthorized", "Unauthorized.");
    }

    public static IResult Forbidden(string message)
    {
        return Build(
            StatusCodes.Status403Forbidden,
            "forbidden",
            string.IsNullOrWhiteSpace(message) ? "Forbidden." : message);
    }

    public static IResult Conflict(string message)
    {
        return Build(
            StatusCodes.Status409Conflict,
            "conflict",
            string.IsNullOrWhiteSpace(message) ? "Conflict." : message);
    }

    public static IResult Internal(string? message = null)
    {
        return Build(
            StatusCodes.Status500InternalServerError,
            "internal_server_error",
            string.IsNullOrWhiteSpace(message)
                ? "An unexpected error occurred."
                : message);
    }

    public static IResult Validation(
        string message,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        return Build(
            StatusCodes.Status400BadRequest,
            "validation_error",
            string.IsNullOrWhiteSpace(message) ? "Invalid input." : message,
            fields);
    }

    private static IResult Build(
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        return new ErrorEnvelopeResult(statusCode, code, message, fields);
    }

    private sealed class ErrorEnvelopeResult : IResult
    {
        private readonly int _statusCode;
        private readonly string _code;
        private readonly string _message;
        private readonly IReadOnlyDictionary<string, string>? _fields;

        public ErrorEnvelopeResult(
            int statusCode,
            string code,
            string message,
            IReadOnlyDictionary<string, string>? fields)
        {
            _statusCode = statusCode;
            _code = code;
            _message = message;
            _fields = fields;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var serializerOptions = httpContext.RequestServices
                .GetService<IOptions<JsonOptions>>()?
                .Value?
                .SerializerOptions
                ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            var payload = new ErrorEnvelopeDto
            {
                Error = new ErrorDetailsDto
                {
                    Code = _code,
                    Message = _message,
                    Fields = _fields is null ? null : new Dictionary<string, string>(_fields)
                },
                TraceId = traceId
            };

            httpContext.Response.StatusCode = _statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsJsonAsync(payload, serializerOptions, httpContext.RequestAborted);
        }
    }

    private sealed class ErrorEnvelopeDto
    {
        public required ErrorDetailsDto Error { get; init; }
        public required string TraceId { get; init; }
    }

    private sealed class ErrorDetailsDto
    {
        public required string Code { get; init; }
        public required string Message { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Fields { get; init; }
    }
}
