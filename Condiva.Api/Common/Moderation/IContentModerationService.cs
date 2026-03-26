namespace Condiva.Api.Common.Moderation;

public interface IContentModerationService
{
    Task<ContentModerationEvaluation> EvaluateAsync(
        string communityId,
        IEnumerable<string?> texts,
        CancellationToken cancellationToken = default);
}

public sealed record ContentModerationEvaluation(
    bool HasMatch,
    bool ShouldBlock,
    IReadOnlyList<string> MatchedTerms);
