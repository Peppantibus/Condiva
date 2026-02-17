namespace Condiva.Api.Common.Mapping;

public sealed class MapperRegistry
{
    private readonly Dictionary<(Type Source, Type Destination), Delegate> _mappings = new();

    public void Register<TSource, TDestination>(Func<TSource, TDestination> mapFn)
    {
        Register<TSource, TDestination>((source, _) => mapFn(source));
    }

    public void Register<TSource, TDestination>(Func<TSource, IServiceProvider, TDestination> mapFn)
    {
        if (mapFn is null)
        {
            throw new ArgumentNullException(nameof(mapFn));
        }
        _mappings[(typeof(TSource), typeof(TDestination))] = mapFn;
    }

    public Func<TSource, IServiceProvider, TDestination> Get<TSource, TDestination>()
    {
        if (_mappings.TryGetValue((typeof(TSource), typeof(TDestination)), out var map))
        {
            return (Func<TSource, IServiceProvider, TDestination>)map;
        }

        throw new InvalidOperationException(
            $"Mapping not registered: {typeof(TSource).Name} -> {typeof(TDestination).Name}");
    }
}
