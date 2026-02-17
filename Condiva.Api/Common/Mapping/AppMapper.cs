namespace Condiva.Api.Common.Mapping;

public sealed class AppMapper : IMapper
{
    private readonly MapperRegistry _registry;
    private readonly IServiceProvider _serviceProvider;

    public AppMapper(MapperRegistry registry, IServiceProvider serviceProvider)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
    }

    public void Register<TSource, TDestination>(Func<TSource, TDestination> mapFn)
    {
        _registry.Register(mapFn);
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        var mapFn = _registry.Get<TSource, TDestination>();
        return mapFn(source, _serviceProvider);
    }

    public IEnumerable<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        var mapFn = _registry.Get<TSource, TDestination>();
        foreach (var item in source)
        {
            yield return mapFn(item, _serviceProvider);
        }
    }
}
