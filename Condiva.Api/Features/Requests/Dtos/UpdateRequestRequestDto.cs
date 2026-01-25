namespace Condiva.Api.Features.Requests.Dtos;

public sealed record UpdateRequestRequestDto(
    string CommunityId,
    string RequesterUserId,
    string Title,
    string Description,
    string Status,
    DateTime? NeededFrom,
    DateTime? NeededTo);
