using System.Net;
using System.Text;
using System.Text.Json;
using Tracker.Importer;
using Tracker.Search;

namespace Tracker.Tests;

public class ColumnMapperTests
{
    private sealed class QueueHandler(params string[] modelOutputs) : HttpMessageHandler
    {
        public List<string> Prompts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Prompts.Add(JsonDocument.Parse(body).RootElement.GetProperty("prompt").GetString()!);

            var output = modelOutputs[Math.Min(Prompts.Count - 1, modelOutputs.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { response = output, done = true }),
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private static readonly Table Sample = new(1, 1,
        [
            new Column("№", 50, 55), new Column("Клас", 75, 85), new Column("Имена", 88, 200),
            new Column("Фик.№2", 579, 594), new Column("Общ бр. точки", 700, 710)
        ],
        [
            new TableRow(["1", "11", "Петър Иванов Колев", "250", "42"]),
            new TableRow(["2", "11", "Анна Георгиева Иванова", "262", "37"]),
            new TableRow(["3", "10", "Мария Тодорова Петрова", "219", "30"])
        ]);

    private const string Correct = """{"name":"Имена","grade":"Клас","score":"Общ бр. точки","place":null}""";

    private static ColumnMapper Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new OllamaOptions { Url = "http://localhost:11434", Model = "test-model" });

    [Fact]
    public async Task Map_ValidAnswer_ReturnsIndexes()
    {
        var map = await Create(new QueueHandler(Correct)).MapAsync(Sample);

        Assert.Equal(new ColumnMap(2, 1, 4, null), map);
    }

    [Fact]
    public async Task Map_UnknownLabel_RetriesThenNull()
    {
        var handler = new QueueHandler("""{"name":"Имена","grade":null,"score":"Точки","place":null}""");

        Assert.Null(await Create(handler).MapAsync(Sample));
        Assert.Equal(2, handler.Prompts.Count);
    }

    [Fact]
    public async Task Map_ScoreColumnNotRanked_RetriesWithCorrection()
    {
        // 250, 262, 219 are fictitious work numbers, not totals.
        var handler = new QueueHandler(
            """{"name":"Имена","grade":"Клас","score":"Фик.№2","place":null}""", Correct);

        var map = await Create(handler).MapAsync(Sample);

        Assert.Equal(4, map!.Score);
        Assert.Equal(2, handler.Prompts.Count);
        Assert.Contains("Фик.№2", handler.Prompts[1][..handler.Prompts[1].IndexOf("Колони:", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task Map_ScoreColumnNotNumbers_Null()
    {
        var handler = new QueueHandler("""{"name":"Имена","grade":null,"score":"Имена","place":null}""");

        Assert.Null(await Create(handler).MapAsync(Sample));
    }

    [Fact]
    public async Task Map_InvalidJsonTwice_Null()
    {
        var handler = new QueueHandler("not json");

        Assert.Null(await Create(handler).MapAsync(Sample));
        Assert.Equal(2, handler.Prompts.Count);
    }

    [Fact]
    public async Task Map_PromptHasLabelsAndSampleRows()
    {
        var handler = new QueueHandler(Correct);

        await Create(handler).MapAsync(Sample);

        foreach (var column in Sample.Columns)
        {
            Assert.Contains(column.Label, handler.Prompts[0]);
        }
        Assert.Contains("Общ бр. точки=42", handler.Prompts[0]);
    }
}
