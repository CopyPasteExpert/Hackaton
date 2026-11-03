using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracker.Search;

// AiUsed: the model rewrote the text into something different.
public record InterpretedQuery(IReadOnlyList<string> Names, bool AiUsed);

// Asks a local LLM (Ollama) to pull the student's name out of free text and
// turn nicknames into official names. Any failure falls back to the raw text,
// so search keeps working without the model.
public class QueryInterpreter(HttpClient http, IOptions<OllamaOptions> options)
{
    private const string Prompt = """
        Ти си помощник за търсене на ученици по име.
        От текста на потребителя извлечи името на ученика (собствено име и фамилия, ако има).
        Ако собственото име е умалително или галено, замени го с официалното българско име.
        Фамилията остави точно както е написана.
        Не добавяй имена или фамилии, които ги няма в текста.
        Върни само JSON във вида {"names": ["Име Фамилия"]}. Ако в текста няма име, върни {"names": []}.

        Примери:
        Текст: Гошо Иванов -> {"names": ["Георги Иванов"]}
        Текст: казвам се Мими Петрова -> {"names": ["Мария Петрова"]}
        Текст: Сашо -> {"names": ["Александър"]}
        Текст: Краси Ковачев -> {"names": ["Красимир Ковачев"]}
        Текст: Ivan Ivanov -> {"names": ["Иван Иванов"]}

        Текст:
        """;

    private readonly OllamaOptions _options = options.Value;

    public async Task<InterpretedQuery> InterpretAsync(string text, CancellationToken ct = default)
    {
        var fallback = new InterpretedQuery([text], false);

        if (!_options.Enabled)
        {
            return fallback;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var names = await AskModelAsync(Prompt + text, timeout.Token);

            if (names.Count == 0)
            {
                return fallback;
            }

            var original = NameNormalizer.Normalize(text);
            var changed = names.Any(n => NameNormalizer.Normalize(n) != original);

            return new InterpretedQuery(names, changed);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return fallback;
        }
    }

    // Loads the model into memory so the first real search is not slowed down
    // by a cold start. Errors are ignored: search works without the model.
    public async Task WarmUpAsync()
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await AskModelAsync(Prompt + "Иван Петров", timeout.Token);
        }
        catch (Exception)
        {
        }
    }

    private async Task<IReadOnlyList<string>> AskModelAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = _options.Model,
            prompt,
            format = "json",
            stream = false,
            keep_alive = "30m",
            options = new { temperature = 0 }
        };

        using var response = await http.PostAsJsonAsync(
            $"{_options.Url.TrimEnd('/')}/api/generate", request, ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var modelOutput = body.GetProperty("response").GetString() ?? "";

        using var parsed = JsonDocument.Parse(modelOutput);

        if (!parsed.RootElement.TryGetProperty("names", out var names)
            || names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return names.EnumerateArray()
            .Where(n => n.ValueKind == JsonValueKind.String)
            .Select(n => n.GetString()!.Trim())
            .Where(n => n.Length > 0)
            .ToList();
    }
}
