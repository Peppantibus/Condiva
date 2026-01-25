namespace Condiva.Api.Features.Communities.Dtos;

public sealed record InviteCodeResponseDto(
    string EnterCode,
    DateTime ExpiresAt);
