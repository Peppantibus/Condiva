using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Dashboard.Dtos;

public sealed record DashboardCountersDto(
    int OpenRequestsTotal,
    int AvailableItemsTotal,
    int MyRequestsTotal);

public sealed record DashboardPreviewItemDto(
    string Id,
    string Title,
    string Status,
    UserSummaryDto Owner,
    DateTime Date,
    string? ThumbnailUrl,
    string[]? AllowedActions = null);

public sealed record DashboardSummaryDto(
    IReadOnlyList<DashboardPreviewItemDto> OpenRequestsPreview,
    IReadOnlyList<DashboardPreviewItemDto> AvailableItemsPreview,
    IReadOnlyList<DashboardPreviewItemDto> MyRequestsPreview,
    DashboardCountersDto Counters);
