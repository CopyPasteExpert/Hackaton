using System.Net;
using System.Text;
using System.Text.Json;
using Tracker.Importer;
using Tracker.Search;

namespace Tracker.Tests;

public class RowExtractorTests
{
    private sealed class QueueHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public int Requests => _next;
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return responses[Math.Min(_next++, responses.Length - 1)]();
        }
    }

    private static Func<HttpResponseMessage> ModelSays(string output) => () =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { response = output, done = true }),
                Encoding.UTF8, "application/json")
        };

    private static Func<HttpResponseMessage> ServerError() => () =>
        new HttpResponseMessage(HttpStatusCode.InternalServerError);

    private const string ValidOutput = """
        {"rows":[{"name":"А Б","grade":11,"score":42,"place":1},{"name":"В Г","grade":null,"score":"11,5","place":null}]}
        """;

    private static RowExtractor Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new OllamaOptions { Url = "http://localhost:11434", Model = "test-model" });

    [Fact]
    public async Task Extract_ValidResponse_ReturnsRows()
    {
        var rows = await Create(new QueueHandler(ModelSays(ValidOutput))).ExtractAsync("текст");

        Assert.Equal(
            [new ExtractedRow("А Б", 11, 42, 1), new ExtractedRow("В Г", null, 11.5m, null)],
            rows);
    }

    [Fact]
    public async Task Extract_InvalidJsonTwice_Throws()
    {
        var handler = new QueueHandler(ModelSays("not json"), ModelSays("still not json"));

        await Assert.ThrowsAsync<ExtractionFailedException>(() => Create(handler).ExtractAsync("текст"));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Extract_InvalidThenValid_ReturnsRows()
    {
        var handler = new QueueHandler(ModelSays("not json"), ModelSays(ValidOutput));

        var rows = await Create(handler).ExtractAsync("текст");

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Extract_HttpErrorTwice_Throws()
    {
        var handler = new QueueHandler(ServerError(), ServerError());

        await Assert.ThrowsAsync<ExtractionFailedException>(() => Create(handler).ExtractAsync("текст"));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Extract_SendsModelAndJsonFormat()
    {
        var handler = new QueueHandler(ModelSays(ValidOutput));

        await Create(handler).ExtractAsync("текст");

        Assert.Contains("\"model\":\"test-model\"", handler.LastBody);
        Assert.Contains("\"format\":\"json\"", handler.LastBody);
        Assert.Contains("\"num_ctx\":8192", handler.LastBody);
    }
}
