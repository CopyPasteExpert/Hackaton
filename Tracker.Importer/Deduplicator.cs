using Tracker.Search;

namespace Tracker.Importer;

// A protocol lists the same student more than once (laureates, then the full
// ranking). Keeps one row per student, competition and year: the one with a
// place, otherwise the one with more points. Rejected rows pass through as they
// are, so the review CSV still shows them.
public static class Deduplicator
{
    public static List<ReviewRow> Deduplicate(IEnumerable<ReviewRow> rows)
    {
        var result = new List<ReviewRow>();
        var okIndex = new Dictionary<(string, string, int), int>();

        foreach (var row in rows)
        {
            if (row.Status != RowStatus.Ok)
            {
                result.Add(row);
                continue;
            }

            var key = (NameNormalizer.Normalize(row.Name), row.Competition, row.Year);

            if (!okIndex.TryGetValue(key, out var index))
            {
                okIndex[key] = result.Count;
                result.Add(row);
            }
            else if (IsBetter(row, result[index]))
            {
                result[index] = row;
            }
        }

        return result;
    }

    private static bool IsBetter(ReviewRow candidate, ReviewRow current) =>
        (candidate.Place.HasValue, current.Place.HasValue) switch
        {
            (true, false) => true,
            (false, true) => false,
            _ => (candidate.Score ?? 0) > (current.Score ?? 0)
        };
}
