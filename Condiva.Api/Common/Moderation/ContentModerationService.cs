using Condiva.Api.Features.Communities.Models;
using Microsoft.EntityFrameworkCore;

namespace Condiva.Api.Common.Moderation;

public sealed class ContentModerationService : IContentModerationService
{
    private readonly CondivaDbContext _dbContext;

    public ContentModerationService(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ContentModerationEvaluation> EvaluateAsync(
        string communityId,
        IEnumerable<string?> texts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return new ContentModerationEvaluation(false, false, Array.Empty<string>());
        }

        var mode = await _dbContext.Communities
            .AsNoTracking()
            .Where(community => community.Id == communityId)
            .Select(community => community.ContentModerationMode)
            .FirstOrDefaultAsync(cancellationToken);
        if (mode == ContentModerationMode.Off)
        {
            return new ContentModerationEvaluation(false, false, Array.Empty<string>());
        }

        var normalizedContent = ContentModerationNormalizer.NormalizeForMatching(
            string.Join(" ", texts.Where(text => !string.IsNullOrWhiteSpace(text))));
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return new ContentModerationEvaluation(false, false, Array.Empty<string>());
        }

        var terms = await _dbContext.CommunityBannedTerms
            .AsNoTracking()
            .Where(term => term.CommunityId == communityId && term.IsActive)
            .Select(term => new { term.Term, term.NormalizedTerm })
            .ToListAsync(cancellationToken);
        if (terms.Count == 0)
        {
            return new ContentModerationEvaluation(false, false, Array.Empty<string>());
        }

        var matched = terms
            .Where(term =>
                !string.IsNullOrWhiteSpace(term.NormalizedTerm)
                && normalizedContent.Contains(term.NormalizedTerm, StringComparison.Ordinal))
            .Select(term => term.Term)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(term => term, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matched.Count == 0)
        {
            return new ContentModerationEvaluation(false, false, Array.Empty<string>());
        }

        return new ContentModerationEvaluation(
            true,
            mode == ContentModerationMode.Block,
            matched);
    }
}
