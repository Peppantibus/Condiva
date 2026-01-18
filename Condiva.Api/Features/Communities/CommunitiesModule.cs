using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Communities;

public static class CommunitiesModule
{
    public static IServiceCollection AddCommunities(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Community feature services go here.
        return services;
    }
}
