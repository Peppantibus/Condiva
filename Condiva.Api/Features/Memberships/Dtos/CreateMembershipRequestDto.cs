namespace Condiva.Api.Features.Memberships.Dtos;

public sealed record CreateMembershipRequestDto(
    string CommunityId,
    string? EnterCode);
