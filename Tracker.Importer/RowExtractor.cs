using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Tracker.Search;

namespace Tracker.Importer;

public class ExtractionFailedException(string message) : Exception(message);

// Asks the local model to turn the text of one protocol page into rows.
// Nothing it returns is trusted; RowValidator checks every row afterwards.
public class RowExtractor(HttpClient http, OllamaOptions options)
{
    private const int Attempts = 2;

    private const string Prompt = """
        Това е текст от страница от протокол на МОН с резултати от национален кръг на олимпиада.
        Извлечи ВСИЧКИ ученици от таблицата. За всеки върни:
        name (трите имена точно както са в текста),
        grade (клас, цяло число),
        score (числото от колоната „Общ бр. точки“ или „Общ брой точки“; НЕ числото от колоните „Фик.№“ или „Фиктивен номер“, които са номера на работи, и НЕ оценката),
        place (място в класирането, цяло число или null; това е мястото, не оценката).
        Не измисляй ученици.
        Върни само JSON: {"rows": [{"name": "...", "grade": 0, "score": 0, "place": null}]}

        Текст:

        """;

    // wrongScores: totals from an earlier attempt that were not in ranking order,
    // i.e. the model read another column; they are quoted back to it as a correction.
    public async Task<IReadOnlyList<ExtractedRow>> ExtractAsync(
        string pageText, IReadOnlyList<decimal>? wrongScores = null, CancellationToken ct = default)
    {
        string? lastError = null;
        var correction = wrongScores is { Count: > 0 }
            ? "Внимание: при предишен опит върна за score числа като " +
              string.Join(", ", wrongScores.Take(5).Select(s => s.ToString("0.##", CultureInfo.InvariantCulture))) +
              ". Те НЕ са от колоната „Общ бр. точки“: в класирането общите точки намаляват отгоре надолу. " +
              "Вземи score само от колоната „Общ бр. точки“ или „Общ брой точки“.\n\n"
            : "";

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                return await AskModelAsync(correction + Prompt + pageText, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                           or InvalidOperationException && !ct.IsCancellationRequested)
            {
                lastError = ex.Message;
            }
        }

        throw new ExtractionFailedException($"The model did not return valid rows after {Attempts} attempts: {lastError}");
    }

    private async Task<IReadOnlyList<ExtractedRow>> AskModelAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = options.Model,
            prompt,
            format = "json",
            stream = false,
            options = new { temperature = 0, num_ctx = 8192 }
        };

        using var response = await http.PostAsJsonAsync(
            $"{options.Url.TrimEnd('/')}/api/generate", request, ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        using var output = JsonDocument.Parse(body.GetProperty("response").GetString() ?? "");

        return output.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => new ExtractedRow(
                r.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()!.Trim() : "",
                ToInt(Number(r, "grade")),
                Number(r, "score"),
                Number(r, "place")))
            .ToList();
    }

    // The model sometimes writes numbers as strings, with a decimal comma ("11,5").
    private static decimal? Number(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(value.GetString()!.Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? ToInt(decimal? value) =>
        value is { } v && v == decimal.Truncate(v) ? (int)v : null;
}
