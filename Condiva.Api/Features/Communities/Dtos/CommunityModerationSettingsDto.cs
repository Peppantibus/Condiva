namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityModerationSettingsDto(
    string CommunityId,
    string Mode,
    IReadOnlyList<CommunityModerationTermDto> Terms);
