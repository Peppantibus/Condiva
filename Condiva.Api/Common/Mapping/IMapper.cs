namespace Condiva.Api.Common.Mapping;

public interface IMapper
{
    void Register<TSource, TDestination>(Func<TSource, TDestination> mapFn);
    TDestination Map<TSource, TDestination>(TSource source);
    IEnumerable<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source);
}
