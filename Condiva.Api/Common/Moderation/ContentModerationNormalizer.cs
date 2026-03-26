using System.Globalization;
using System.Text;

namespace Condiva.Api.Common.Moderation;

public static class ContentModerationNormalizer
{
    public static string NormalizeForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var raw in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalized = NormalizeChar(raw);
            if (normalized.HasValue)
            {
                builder.Append(normalized.Value);
            }
        }

        return builder.ToString();
    }

    private static char? NormalizeChar(char value)
    {
        var lower = char.ToLowerInvariant(value);
        return lower switch
        {
            '@' or '4' => 'a',
            '3' => 'e',
            '1' or '!' => 'i',
            '0' => 'o',
            '5' or '$' => 's',
            '7' => 't',
            _ when char.IsLetterOrDigit(lower) => lower,
            _ => null
        };
    }
}
