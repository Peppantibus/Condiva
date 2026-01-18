using Condiva.Api.Features.Communities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCondivaServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCommunities(configuration);

        return services;
    }
}
