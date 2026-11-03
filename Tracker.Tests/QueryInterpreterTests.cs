using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tracker.Search;

namespace Tracker.Tests;

public class QueryInterpreterTests
{
    private sealed class FakeHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return respond(cancellationToken);
        }
    }

    private static FakeHandler RespondWithModelOutput(string modelOutput) =>
        new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { response = modelOutput, done = true }),
                Encoding.UTF8, "application/json")
        }));

    private static QueryInterpreter Create(
        HttpMessageHandler handler, bool enabled = true, int timeoutSeconds = 3) =>
        new(new HttpClient(handler),
            Options.Create(new OllamaOptions
            {
                Enabled = enabled,
                Url = "http://localhost:11434",
                Model = "test-model",
                TimeoutSeconds = timeoutSeconds
            }));

    private static void AssertFallback(InterpretedQuery result, string text)
    {
        Assert.Equal([text], result.Names);
        Assert.False(result.AiUsed);
    }

    [Fact]
    public async Task Interpret_Nickname_ReturnsOfficialName()
    {
        var handler = RespondWithModelOutput("{\"names\":[\"Иван Петров\"]}");

        var result = await Create(handler).InterpretAsync("Ваньо Петров");

        Assert.Equal(["Иван Петров"], result.Names);
        Assert.True(result.AiUsed);
    }

    [Fact]
    public async Task Interpret_SameName_NotAiUsed()
    {
        var handler = RespondWithModelOutput("{\"names\":[\"Иван Петров\"]}");

        var result = await Create(handler).InterpretAsync("иван петров");

        Assert.False(result.AiUsed);
    }

    [Fact]
    public async Task Interpret_InvalidJson_FallsBack()
    {
        var handler = RespondWithModelOutput("Иван Петров, разбира се!");

        AssertFallback(await Create(handler).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_EmptyNames_FallsBack()
    {
        var handler = RespondWithModelOutput("{\"names\":[\"\", \" \"]}");

        AssertFallback(await Create(handler).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_WrongShape_FallsBack()
    {
        var handler = RespondWithModelOutput("{\"name\":\"Иван\"}");

        AssertFallback(await Create(handler).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_HttpError_FallsBack()
    {
        var handler = new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        AssertFallback(await Create(handler).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_ConnectionRefused_FallsBack()
    {
        var handler = new FakeHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        AssertFallback(await Create(handler).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_Timeout_FallsBack()
    {
        var handler = new FakeHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        AssertFallback(
            await Create(handler, timeoutSeconds: 1).InterpretAsync("Ваньо"), "Ваньо");
    }

    [Fact]
    public async Task Interpret_Disabled_NoRequest()
    {
        var handler = RespondWithModelOutput("{\"names\":[\"Иван\"]}");

        var result = await Create(handler, enabled: false).InterpretAsync("Ваньо");

        AssertFallback(result, "Ваньо");
        Assert.Equal(0, handler.Requests);
    }
}
