namespace Tracker.Search;

public record NameMatch(string Name, double Score);

public record MatchResult(IReadOnlyList<NameMatch> Matches, string? DidYouMean);

// Ranks known student names by how close they are to what the user typed.
public static class NameMatcher
{
    public const double MatchThreshold = 0.85;
    public const double SuggestThreshold = 0.70;

    // A suggestion needs every query word to resemble some word of the name;
    // a shared "-ова" ending alone must not produce "Имахте предвид…?".
    public const double SuggestMinWordSimilarity = 0.75;

    // A match needs every query word to be close, not just a good average: an
    // exact first name plus a surname sharing only "-ова" is another student.
    public const double MatchMinWordSimilarity = 0.85;

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
            .Select(name =>
            {
                var (average, weakest) = Score(queryWords, name);
                return (Match: new NameMatch(name, average), Weakest: weakest);
            })
            .OrderByDescending(s => s.Match.Score)
            .ThenBy(s => s.Match.Name, StringComparer.Ordinal)
            .ToList();

        var matches = scored
            .Where(s => s.Match.Score >= MatchThreshold && s.Weakest >= MatchMinWordSimilarity)
            .Select(s => s.Match)
            .ToList();

        string? didYouMean = null;

        if (matches.Count == 0)
        {
            didYouMean = scored
                .Where(s => s.Match.Score >= SuggestThreshold
                    && s.Weakest >= SuggestMinWordSimilarity)
                .Select(s => s.Match.Name)
                .FirstOrDefault();
        }

        return new MatchResult(matches, didYouMean);
    }

    // Pairs each query word with a different word of the candidate, best pairs
    // first, so word order does not matter but one candidate word cannot stand
    // in for two query words ("Иван Иванов" is not "Иван Петров"). Returns the
    // average similarity over the query words (unpaired words count as 0) and
    // the weakest single word.
    private static (double Average, double Weakest) Score(string[] queryWords, string candidate)
    {
        var candidateWords = NameNormalizer.Normalize(candidate)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (candidateWords.Length == 0)
        {
            return (0, 0);
        }

        var pairs =
            from q in Enumerable.Range(0, queryWords.Length)
            from c in Enumerable.Range(0, candidateWords.Length)
            select (Query: q, Candidate: c, Similarity: JaroWinkler(queryWords[q], candidateWords[c]));

        var best = new double[queryWords.Length];
        var queryUsed = new bool[queryWords.Length];
        var candidateUsed = new bool[candidateWords.Length];

        foreach (var pair in pairs.OrderByDescending(p => p.Similarity))
        {
            if (queryUsed[pair.Query] || candidateUsed[pair.Candidate])
            {
                continue;
            }

            queryUsed[pair.Query] = candidateUsed[pair.Candidate] = true;
            best[pair.Query] = pair.Similarity;
        }

        return (best.Average(), best.Min());
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
