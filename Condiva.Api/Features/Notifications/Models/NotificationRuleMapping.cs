namespace Condiva.Api.Features.Notifications.Models;

public sealed record NotificationRuleMapping(
    string EntityType,
    string Action,
    List<NotificationType> Types);
