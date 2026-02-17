using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Requests.Dtos;

namespace Condiva.Api.Features.Dashboard.Dtos;

public sealed record DashboardCountersDto(
    int OpenRequestsTotal,
    int AvailableItemsTotal,
    int MyRequestsTotal);

public sealed record DashboardSummaryDto(
    IReadOnlyList<RequestListItemDto> OpenRequestsPreview,
    IReadOnlyList<ItemListItemDto> AvailableItemsPreview,
    IReadOnlyList<RequestListItemDto> MyRequestsPreview,
    DashboardCountersDto Counters);
