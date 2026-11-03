namespace Tracker.Search;

public record NameMatch(string Name, double Score);

public record MatchResult(IReadOnlyList<NameMatch> Matches, string? DidYouMean);

// Ranks known student names by how close they are to what the user typed.
public static class NameMatcher
{
    public const double MatchThreshold = 0.85;
    public const double SuggestThreshold = 0.70;

    // Query words shorter than this carry too little signal ("и", "a").
    private const int MinWordLength = 2;

    public static MatchResult Match(string query, IEnumerable<string> candidates)
    {
        var queryWords = NameNormalizer.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= MinWordLength)
            .ToArray();

        if (queryWords.Length == 0)
        {
            return new MatchResult([], null);
        }

        var scored = candidates
            .Distinct()
            .Select(name => new NameMatch(name, Score(queryWords, name)))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var matches = scored.Where(m => m.Score >= MatchThreshold).ToList();

        string? didYouMean = null;

        if (matches.Count == 0 && scored.Count > 0
            && scored[0].Score >= SuggestThreshold)
        {
            didYouMean = scored[0].Name;
        }

        return new MatchResult(matches, didYouMean);
    }

    // Average, over the query words, of each word's best similarity to any
    // word of the candidate. Word order and missing words do not matter.
    private static double Score(string[] queryWords, string candidate)
    {
        var candidateWords = NameNormalizer.Normalize(candidate)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (candidateWords.Length == 0)
        {
            return 0;
        }

        return queryWords.Average(q => candidateWords.Max(c => JaroWinkler(q, c)));
    }

    public static double JaroWinkler(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var window = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var from = Math.Max(0, i - window);
            var to = Math.Min(b.Length - 1, i + window);

            for (var j = from; j <= to; j++)
            {
                if (bMatched[j] || a[i] != b[j])
                {
                    continue;
                }

                aMatched[i] = bMatched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
        {
            return 0;
        }

        var transpositions = 0;
        var k = 0;

        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        double m = matches;
        var jaro = (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3;

        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
