namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityRolePermissionsDto(
    string CommunityId,
    string Role,
    string[] Permissions);
