using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Condiva.Api.Common.Errors;

public static class ApiErrors
{
    public static IResult Required(string field)
    {
        return HttpResults.BadRequest(new { error = $"{field} is required.", code = "required", field });
    }

    public static IResult Invalid(string message)
    {
        return HttpResults.BadRequest(new { error = message, code = "invalid" });
    }

    public static IResult NotFound(string entity)
    {
        return HttpResults.NotFound(new { error = $"{entity} not found.", code = "not_found", entity });
    }

    public static IResult Unauthorized()
    {
        return HttpResults.Json(
            new { error = "Unauthorized.", code = "unauthorized" },
            statusCode: StatusCodes.Status401Unauthorized);
    }
}
