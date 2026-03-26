namespace Condiva.Api.Features.Storage.Dtos;

public sealed record StoragePurgeAllResponseDto(int DeletedObjects, DateTime ExecutedAtUtc);
