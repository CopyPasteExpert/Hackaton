using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tracker.Search;

namespace Tracker.Importer;

// Asks the local model, once per table, which columns hold the name, grade,
// total score and place. Header wording differs between protocols ("Общ бр.
// точки", "Общо", "Резултат", "Приз"); the values themselves are read by code.
public class ColumnMapper(HttpClient http, OllamaOptions options)
{
    private const int Attempts = 2;
    private const int SampleRows = 3;
    private const double MinNumericShare = 0.9;
    private const double MaxRiseShare = 0.1;

    private const string Instructions = """
        Това е таблица от протокол на МОН с резултати от олимпиада.
        Кажи кои колони съдържат:
        name: трите имена на ученика;
        grade: класа (или null, ако няма такава колона);
        score: общия брой точки (НЕ „Фик.№“/„Фиктивен номер“, НЕ точките по отделна задача, НЕ оценката);
        place: мястото в класирането или наградата (или null, ако няма такава колона).
        Отговори с етикетите на колоните точно както са дадени.
        Върни само JSON: {"name": "...", "grade": "..." или null, "score": "...", "place": "..." или null}


        """;

    private record Answer(string? Name, string? Grade, string? Score, string? Place);

    // Why the last MapAsync returned null, for the extract log.
    public string? LastProblem { get; private set; }

    public async Task<ColumnMap?> MapAsync(Table table, CancellationToken ct = default)
    {
        var correction = "";
        LastProblem = null;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            Answer answer;

            try
            {
                answer = await AskAsync(correction + Instructions + Describe(table), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                           or InvalidOperationException && !ct.IsCancellationRequested)
            {
                LastProblem = $"no valid answer from the model ({ex.Message})";
                continue;
            }

            var (map, problem) = Check(table, answer);

            if (map != null)
            {
                LastProblem = null;
                return map;
            }

            LastProblem = problem;
            correction = $"Внимание: предишният отговор не е верен: {problem}\n\n";
        }

        return null;
    }

    private static string Describe(Table table)
    {
        var text = new StringBuilder("Колони:\n");

        foreach (var column in table.Columns)
        {
            text.Append("- ").Append(column.Label).Append('\n');
        }

        text.Append("\nПримерни редове:\n");

        foreach (var row in table.Rows.Take(SampleRows))
        {
            text.AppendJoin(" | ", table.Columns
                .Select((c, i) => (c.Label, Value: row.Values[i]))
                .Where(x => x.Value.Length > 0)
                .Select(x => $"{x.Label}={x.Value}"));
            text.Append('\n');
        }

        return text.ToString();
    }

    private async Task<Answer> AskAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = options.Model,
            prompt,
            format = "json",
            stream = false,
            options = new { temperature = 0, num_ctx = 8192 }
        };

        using var response = await http.PostAsJsonAsync($"{options.Url.TrimEnd('/')}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var output = JsonDocument.Parse(body.GetProperty("response").GetString() ?? "");
        var root = output.RootElement;

        string? Label(string property) =>
            root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!.Trim()
                : null;

        return new Answer(Label("name"), Label("grade"), Label("score"), Label("place"));
    }

    // The answer must name existing columns, and the score column must hold
    // numbers that go down the table like a ranking.
    private static (ColumnMap? Map, string? Problem) Check(Table table, Answer answer)
    {
        // Exact label first. Otherwise the one column whose label is part of the
        // answer or the other way round: a header cut to "брой" while the model
        // answers "Общ брой точки". The value checks below still apply.
        int? Index(string? label)
        {
            if (label == null)
            {
                return null;
            }

            var exact = table.Columns.Select((c, i) => (c, i)).Where(x => x.c.Label == label).Select(x => (int?)x.i).FirstOrDefault();

            if (exact != null)
            {
                return exact;
            }

            var partial = table.Columns.Select((c, i) => (c, i))
                .Where(x => x.c.Label.Length >= 3
                            && (label.Contains(x.c.Label, StringComparison.OrdinalIgnoreCase)
                                || x.c.Label.Contains(label, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return partial.Count == 1 ? partial[0].i : null;
        }

        var name = Index(answer.Name);
        var score = Index(answer.Score);

        if (name == null)
        {
            return (null, $"колоната за name „{answer.Name}“ я няма в таблицата.");
        }

        if (score == null)
        {
            return (null, $"колоната за score „{answer.Score}“ я няма в таблицата.");
        }

        var scores = table.Rows.Select(r => ProtocolNumber.Parse(r.Values[score.Value])).ToList();

        if (scores.Count(s => s.HasValue) < MinNumericShare * scores.Count)
        {
            return (null, $"в колоната „{answer.Score}“ няма числа, а score е общият брой точки.");
        }

        // One table, so totals should not rise at all; allow a few rises (10%)
        // for odd rows. Fictitious numbers rise about half of the time.
        var known = scores.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var rises = known.Zip(known.Skip(1)).Count(p => p.Second > p.First);

        if (rises > MaxRiseShare * known.Count)
        {
            return (null, $"„{answer.Score}“ не е общият брой точки: стойностите там не намаляват отгоре надолу като в класиране.");
        }

        return (new ColumnMap(name.Value, Index(answer.Grade), score.Value, Index(answer.Place)), null);
    }
}
