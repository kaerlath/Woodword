using System.Text.RegularExpressions;

namespace Woodword.Services;

internal static partial class IcelandicDetector
{
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "að", "af", "á", "bara", "ef", "ég", "ekki", "en", "er", "frá", "fyrir",
        "hafa", "hann", "hefur", "hér", "hvernig", "hún", "hvað", "hver", "í", "líka",
        "með", "mjög", "mín", "minn", "mun", "og", "sem", "skal", "svo", "það", "þar",
        "þegar", "þær", "þeir", "þið", "þín", "þinn", "til", "um", "var", "vera", "við", "þú",
    };

    public static bool IsLikelyIcelandic(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 8) return false;

        var words = WordPattern().Matches(text).Select(match => match.Value).ToArray();
        if (words.Length < 2) return false;

        var distinctiveLetters = text.Count(character => character is 'ð' or 'Ð' or 'þ' or 'Þ');
        var accentedLetters = text.Count(character => "áÁéÉíÍóÓúÚýÝæÆöÖ".Contains(character));
        var commonWordHits = words.Count(word => CommonWords.Contains(word));

        // Deliberately conservative: uncertain text stays entirely on the client.
        return (distinctiveLetters >= 1 && (commonWordHits >= 1 || distinctiveLetters >= 2))
            || (commonWordHits >= 3 && accentedLetters >= 1)
            || commonWordHits >= 4;
    }

    [GeneratedRegex(@"[\p{L}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
