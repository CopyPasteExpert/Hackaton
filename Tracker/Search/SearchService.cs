using MySqlConnector;

namespace Tracker.Search;

public record StudentResult(int Id, string Competition, int Year, decimal? Score);

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
        var interpreted = await interpreter.InterpretAsync(q, ct);

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

    // Matches both the raw text and the model's reading of it, keeping each
    // name's best score. The model gets credit (AiUsed, AiNote) only when it
    // changed the outcome: its best match is not what the raw text finds first.
    // This way a wrong guess by the model never hides a plain fuzzy match, and
    // the note always names a real student rather than the model's raw output.
    public static CombinedMatch Combine(
        string query, InterpretedQuery interpreted, IReadOnlyList<string> candidates)
    {
        var raw = NameMatcher.Match(query, candidates);

        var best = raw.Matches.ToDictionary(m => m.Name, m => m.Score);
        string? aiDidYouMean = null;

        if (interpreted.AiUsed)
        {
            foreach (var name in interpreted.Names)
            {
                var result = NameMatcher.Match(name, candidates);
                aiDidYouMean ??= result.DidYouMean;

                foreach (var match in result.Matches)
                {
                    best[match.Name] = Math.Max(best.GetValueOrDefault(match.Name), match.Score);
                }
            }
        }

        var matches = best
            .Select(kv => new NameMatch(kv.Key, kv.Value))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        if (matches.Count == 0)
        {
            return new CombinedMatch([], raw.DidYouMean ?? aiDidYouMean, false, null);
        }

        var aiChangedOutcome = interpreted.AiUsed
            && (raw.Matches.Count == 0 || raw.Matches[0].Name != matches[0].Name);

        return aiChangedOutcome
            ? new CombinedMatch(matches, null, true, $"Разбрах „{query}“ като „{matches[0].Name}“")
            : new CombinedMatch(matches, null, false, null);
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
            SELECT id, participant_name, competition_name, competition_year, score
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
                reader.IsDBNull(reader.GetOrdinal("score")) ? null : reader.GetDecimal("score")));
        }

        return byName;
    }
}
