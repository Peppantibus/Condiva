using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Events.Dtos;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Loans.Dtos;
using Condiva.Api.Features.Memberships.Dtos;
using Condiva.Api.Features.Notifications.Dtos;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Reputations.Dtos;
using Condiva.Api.Features.Requests.Dtos;

namespace Condiva.Api.Common.Mapping;

public static class MappingRegistration
{
    public static void RegisterAll(MapperRegistry registry)
    {
        CommunityMappings.Register(registry);
        EventMappings.Register(registry);
        ItemMappings.Register(registry);
        LoanMappings.Register(registry);
        MembershipMappings.Register(registry);
        NotificationMappings.Register(registry);
        OfferMappings.Register(registry);
        ReputationMappings.Register(registry);
        RequestMappings.Register(registry);
    }
}
