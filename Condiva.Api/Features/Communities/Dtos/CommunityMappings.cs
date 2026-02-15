using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Models;

namespace Condiva.Api.Features.Communities.Dtos;

public static class CommunityMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Community, CommunityListItemDto>(community => new CommunityListItemDto(
            community.Id,
            community.Name,
            community.Slug,
            community.Description,
            community.ImageKey,
            community.CreatedByUserId,
            community.CreatedAt));

        registry.Register<Community, CommunityDetailsDto>(community => new CommunityDetailsDto(
            community.Id,
            community.Name,
            community.Slug,
            community.Description,
            community.ImageKey,
            community.CreatedByUserId,
            community.CreatedAt));

        registry.Register<InviteCodeInfo, InviteCodeResponseDto>(info => new InviteCodeResponseDto(
            info.EnterCode,
            info.ExpiresAt));
    }
}
