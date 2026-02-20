namespace Condiva.Api.Features.Requests.Dtos;

public sealed record RequestCountsDto(
    int Open,
    int MyOpen,
    int ExpiringSoon);
