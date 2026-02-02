namespace Condiva.Api.Features.Notifications.Dtos;

public sealed record NotificationMarkReadRequestDto(
    List<string> Ids);
