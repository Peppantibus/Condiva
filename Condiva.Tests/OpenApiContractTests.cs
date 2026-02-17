using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Condiva.Tests.Infrastructure;

namespace Condiva.Tests;

public sealed class OpenApiContractTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public OpenApiContractTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Swagger_UnreadCountEndpoint_IsTyped()
    {
        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var operation = GetOperation(root, "/api/notifications/unread-count", "get");
        var schema = GetSuccessSchema(root, operation);

        var resolvedSchema = ResolveSchema(root, schema);
        Assert.True(resolvedSchema.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("unreadCount", out var unreadCountProperty));
        Assert.Equal("integer", unreadCountProperty.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Swagger_ItemsList_UsesUniformPagedContract()
    {
        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var operation = GetOperation(root, "/api/items", "get");
        var schema = ResolveSchema(root, GetSuccessSchema(root, operation));

        Assert.True(schema.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("items", out _));
        Assert.True(properties.TryGetProperty("page", out _));
        Assert.True(properties.TryGetProperty("pageSize", out _));
        Assert.True(properties.TryGetProperty("total", out _));
        Assert.True(properties.TryGetProperty("sort", out _));
        Assert.True(properties.TryGetProperty("order", out _));
    }

    [Fact]
    public async Task Swagger_Login_ExposesCanonicalAuthResponseShape()
    {
        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var operation = GetOperation(root, "/api/auth/login", "post");
        var schema = ResolveSchema(root, GetSuccessSchema(root, operation));

        Assert.True(schema.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("accessToken", out _));
        Assert.True(properties.TryGetProperty("expiresIn", out _));
        Assert.True(properties.TryGetProperty("tokenType", out _));
        Assert.True(properties.TryGetProperty("expiresAt", out _));
        Assert.True(properties.TryGetProperty("refreshTokenExpiresAt", out _));
        Assert.True(properties.TryGetProperty("user", out _));
    }

    private static JsonElement GetOperation(JsonElement root, string path, string method)
    {
        return root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);
    }

    private static JsonElement GetSuccessSchema(JsonElement root, JsonElement operation)
    {
        var responses = operation.GetProperty("responses");
        var successKey = responses.EnumerateObject()
            .Select(entry => entry.Name)
            .First(name => name.StartsWith("2", StringComparison.Ordinal));

        var content = responses
            .GetProperty(successKey)
            .GetProperty("content");

        var mediaType = content.TryGetProperty("application/json", out var jsonContent)
            ? jsonContent
            : content.EnumerateObject().First().Value;

        return mediaType.GetProperty("schema");
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var value = reference.GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));

        var schemaName = value!.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        return root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);
    }
}
