namespace Condiva.Api.Common.Mapping;

public sealed class AppMapper : IMapper
{
    private readonly MapperRegistry _registry;

    public AppMapper(MapperRegistry registry)
    {
        _registry = registry;
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
        return mapFn(source);
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
            yield return mapFn(item);
        }
    }
}
