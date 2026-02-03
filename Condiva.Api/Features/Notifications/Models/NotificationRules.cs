using Condiva.Api.Features.Events.Models;
using Microsoft.EntityFrameworkCore;

namespace Condiva.Api.Features.Notifications.Models;

public sealed class NotificationRules
{
    private readonly CondivaDbContext _dbContext;

    public NotificationRules(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<(string EntityType, string Action), NotificationType[]>> GetMapAsync(
        CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.NotificationRuleMappings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return BuildMap(mappings);
    }

    public IReadOnlyList<NotificationType> GetNotificationTypes(
        Event evt,
        IReadOnlyDictionary<(string EntityType, string Action), NotificationType[]> map)
    {
        if (evt is null)
        {
            return Array.Empty<NotificationType>();
        }

        return map.TryGetValue((evt.EntityType, evt.Action), out var types)
            ? types
            : Array.Empty<NotificationType>();
    }

    private static IReadOnlyDictionary<(string EntityType, string Action), NotificationType[]>
        BuildMap(IEnumerable<NotificationRule> mappings)
    {
        var map = new Dictionary<(string, string), HashSet<NotificationType>>(new EventKeyComparer());
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.EntityType)
                || string.IsNullOrWhiteSpace(mapping.Action)
                || !Enum.IsDefined(typeof(NotificationType), mapping.Type))
            {
                continue;
            }

            var key = (mapping.EntityType, mapping.Action);
            if (!map.TryGetValue(key, out var types))
            {
                types = new HashSet<NotificationType>();
                map[key] = types;
            }

            types.Add(mapping.Type);
        }

        var flattened = new Dictionary<(string, string), NotificationType[]>(map.Count, map.Comparer);
        foreach (var entry in map)
        {
            flattened[entry.Key] = entry.Value.ToArray();
        }

        return flattened;
    }

    private sealed class EventKeyComparer : IEqualityComparer<(string EntityType, string Action)>
    {
        public bool Equals((string EntityType, string Action) x, (string EntityType, string Action) y)
        {
            return StringComparer.Ordinal.Equals(x.EntityType, y.EntityType)
                && StringComparer.Ordinal.Equals(x.Action, y.Action);
        }

        public int GetHashCode((string EntityType, string Action) obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.EntityType),
                StringComparer.Ordinal.GetHashCode(obj.Action));
        }
    }
}
