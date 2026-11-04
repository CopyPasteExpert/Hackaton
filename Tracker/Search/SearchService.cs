using MySqlConnector;

namespace Tracker.Search;

public record StudentResult(int Id, string Competition, int Year, decimal? Score,
    int? Grade, int? Place, string? SourceUrl);

public record StudentMatch(string Name, double MatchScore, IReadOnlyList<StudentResult> Results);

public record SearchResponse(
    IReadOnlyList<StudentMatch> Students, string? DidYouMean, bool AiUsed, string? AiNote);

public record CombinedMatch(
    IReadOnlyList<NameMatch> Matches, string? DidYouMean, bool AiUsed, string? AiNote);

// Free-text name search: model interpretation -> fuzzy match against the names
// in the database -> competition results of the best matches.
public class SearchService(QueryInterpreter interpreter, IConfiguration configuration)
{
    private const int MaxStudents = 5;

    public async Task<SearchResponse> SearchAsync(string q, CancellationToken ct = default)
    {
        // A full name needs no model, and the model would only shorten it.
        var interpreted = IsFullName(q)
            ? new InterpretedQuery([q], false)
            : await interpreter.InterpretAsync(q, ct);

        await using var connection = new MySqlConnection(
            configuration.GetConnectionString("Tracker"));
        await connection.OpenAsync(ct);

        var candidates = await LoadNamesAsync(connection, ct);

        var combined = Combine(q, interpreted, candidates);

        var top = combined.Matches.Take(MaxStudents).ToList();
        var results = await LoadResultsAsync(connection, top.Select(m => m.Name).ToList(), ct);

        var students = top
            .Select(m => new StudentMatch(
                m.Name,
                Math.Round(m.Score, 3),
                results.GetValueOrDefault(m.Name, [])))
            .ToList();

        return new SearchResponse(students, combined.DidYouMean, combined.AiUsed, combined.AiNote);
    }

    // The baseline is the raw text plus its nickname-dictionary readings; the
    // model's readings are matched on top, keeping each name's best score. The
    // model gets credit (AiUsed, AiNote) only when it changed the outcome: its
    // best match is not what the baseline finds first. On a tie the baseline
    // wins, so a confident wrong guess by the model never hides a real match,
    // and the note always names a real student rather than the model's output.
    public static CombinedMatch Combine(
        string query, InterpretedQuery interpreted, IReadOnlyList<string> candidates)
    {
        // Without the model, common words around a name ("казвам се …") would
        // count as name words, so the baseline also tries the query without them.
        var stripped = StripFillerWords(query);
        string[] readings = stripped.Length > 0 && stripped != NameNormalizer.Normalize(query)
            ? [query, stripped, .. NicknameDictionary.Expand(query), .. NicknameDictionary.Expand(stripped)]
            : [query, .. NicknameDictionary.Expand(query)];

        var (baseline, baselineDidYouMean) = MatchAll(readings, candidates);

        // A full name (three or more name words, nothing else) is not given to
        // the model: it drops the middle name, and two names then fit another
        // student ("Петър Иванов Петров" → "Петър Петров Овчаров").
        var useModel = interpreted.AiUsed && !IsFullName(query);

        // The model's suggestion is dropped: it would be built from a name the
        // model may have made up, not from what the user typed.
        var (ai, _) = useModel
            ? MatchAll(interpreted.Names, candidates)
            : ([], null);

        var matches = baseline.Keys.Union(ai.Keys)
            .Select(name => new NameMatch(name,
                Math.Max(baseline.GetValueOrDefault(name), ai.GetValueOrDefault(name))))
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => baseline.ContainsKey(m.Name))
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
        {
            return new CombinedMatch([], baselineDidYouMean, false, null);
        }

        // An exact match hides near ones ("Иван Петров" does not also list
        // "Иван Петков"); without one, close matches all stay.
        if (matches[0].Score >= ExactScore)
        {
            matches = matches.Where(m => m.Score >= ExactScore).ToList();
        }

        var baselineTop = baseline
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        var aiChangedOutcome = useModel && baselineTop != matches[0].Name;

        return aiChangedOutcome
            ? new CombinedMatch(matches, null, true, $"Разбрах „{query}“ като „{matches[0].Name}“")
            : new CombinedMatch(matches, null, false, null);
    }

    // Words people put around their name when they write a sentence. None of
    // them is a Bulgarian name.
    private static readonly HashSet<string> FillerWords =
    [
        "аз", "съм", "казвам", "се", "името", "ми", "е", "моето", "търся", "покажи",
        "резултатите", "резултати", "на", "за", "здравей", "здрасти", "моля", "постиженията"
    ];

    private const double ExactScore = 0.999;

    // Exactly three words and none of them a filler word: what someone types as
    // their full name. A longer query ("колко точки има Гергана Рандева") is a
    // question, which the model may still read.
    public static bool IsFullName(string query)
    {
        var normalized = NameNormalizer.Normalize(query);

        return StripFillerWords(query) == normalized
               && normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 3;
    }

    private static string StripFillerWords(string query) =>
        string.Join(' ', NameNormalizer.Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !FillerWords.Contains(w)));

    // Best score per candidate over several readings of the query.
    private static (Dictionary<string, double> Best, string? DidYouMean) MatchAll(
        IEnumerable<string> readings, IReadOnlyList<string> candidates)
    {
        var best = new Dictionary<string, double>();
        string? didYouMean = null;

        foreach (var reading in readings)
        {
            var result = NameMatcher.Match(reading, candidates);
            didYouMean ??= result.DidYouMean;

            foreach (var match in result.Matches)
            {
                best[match.Name] = Math.Max(best.GetValueOrDefault(match.Name), match.Score);
            }
        }

        return (best, didYouMean);
    }

    private static async Task<List<string>> LoadNamesAsync(
        MySqlConnection connection, CancellationToken ct)
    {
        await using var command = new MySqlCommand(
            "SELECT DISTINCT participant_name FROM competition_results", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var names = new List<string>();

        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<Dictionary<string, List<StudentResult>>> LoadResultsAsync(
        MySqlConnection connection, IReadOnlyList<string> names, CancellationToken ct)
    {
        var byName = new Dictionary<string, List<StudentResult>>();

        if (names.Count == 0)
        {
            return byName;
        }

        var parameters = names.Select((_, i) => $"@n{i}").ToList();

        await using var command = new MySqlCommand($"""
            SELECT id, participant_name, competition_name, competition_year, score, grade, place, source_url
            FROM competition_results
            WHERE participant_name IN ({string.Join(", ", parameters)})
            ORDER BY competition_year DESC, competition_name
            """, connection);

        for (var i = 0; i < names.Count; i++)
        {
            command.Parameters.AddWithValue(parameters[i], names[i]);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString("participant_name");

            if (!byName.TryGetValue(name, out var list))
            {
                byName[name] = list = [];
            }

            list.Add(new StudentResult(
                reader.GetInt32("id"),
                reader.GetString("competition_name"),
                reader.GetInt32("competition_year"),
                reader.IsDBNull(reader.GetOrdinal("score")) ? null : reader.GetDecimal("score"),
                reader.IsDBNull(reader.GetOrdinal("grade")) ? null : reader.GetInt32("grade"),
                reader.IsDBNull(reader.GetOrdinal("place")) ? null : reader.GetInt32("place"),
                reader.IsDBNull(reader.GetOrdinal("source_url")) ? null : reader.GetString("source_url")));
        }

        return byName;
    }
}
