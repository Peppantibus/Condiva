using Condiva.Api.Features.Events.Models;
using Microsoft.Extensions.Configuration;

namespace Condiva.Api.Features.Notifications.Models;

public sealed class NotificationRules
{
    private readonly IReadOnlyDictionary<(string EntityType, string Action), NotificationType[]> _map;

    public NotificationRules(IConfiguration configuration)
    {
        var mappings = configuration.GetSection("NotificationRules:Mappings")
            .Get<List<NotificationRuleMapping>>() ?? new List<NotificationRuleMapping>();
        _map = BuildMap(mappings);
    }

    public IReadOnlyList<NotificationType> GetNotificationTypes(Event evt)
    {
        if (evt is null)
        {
            return Array.Empty<NotificationType>();
        }

        return _map.TryGetValue((evt.EntityType, evt.Action), out var types)
            ? types
            : Array.Empty<NotificationType>();
    }

    private static IReadOnlyDictionary<(string EntityType, string Action), NotificationType[]>
        BuildMap(IEnumerable<NotificationRuleMapping> mappings)
    {
        var map = new Dictionary<(string, string), NotificationType[]>(new EventKeyComparer());
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.EntityType)
                || string.IsNullOrWhiteSpace(mapping.Action)
                || mapping.Types is null
                || mapping.Types.Count == 0)
            {
                continue;
            }

            map[(mapping.EntityType, mapping.Action)] = mapping.Types.ToArray();
        }

        return map;
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
