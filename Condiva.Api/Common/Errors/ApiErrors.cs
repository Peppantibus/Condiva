namespace Condiva.Api.Common.Errors;

public static class ApiErrors
{
    public static IResult Required(string field)
    {
        return Results.BadRequest(new { error = $"{field} is required.", code = "required", field });
    }

    public static IResult Invalid(string message)
    {
        return Results.BadRequest(new { error = message, code = "invalid" });
    }

    public static IResult NotFound(string entity)
    {
        return Results.NotFound(new { error = $"{entity} not found.", code = "not_found", entity });
    }

    public static IResult Unauthorized()
    {
        return Results.Json(
            new { error = "Unauthorized.", code = "unauthorized" },
            statusCode: StatusCodes.Status401Unauthorized);
    }
}
