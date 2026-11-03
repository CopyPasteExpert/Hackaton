using Tracker.Search;

namespace Tracker.Tests;

public class SearchServiceTests
{
    private static readonly string[] Candidates =
    [
        "Иван Петров", "Иван Петков", "Димитър Стоянов", "Стефан Колев", "Мария Иванова"
    ];

    [Fact]
    public void Combine_AiFindsWhatRawCannot_CreditsAi()
    {
        // "Мецо" is not in the nickname dictionary, so only the model can help.
        var result = SearchService.Combine(
            "Мецо", new InterpretedQuery(["Димитър"], true), Candidates);

        Assert.Equal("Димитър Стоянов", result.Matches[0].Name);
        Assert.True(result.AiUsed);
        Assert.Equal("Разбрах „Мецо“ като „Димитър Стоянов“", result.AiNote);
    }

    [Fact]
    public void Combine_DictionaryNickname_FoundWithoutAi()
    {
        var result = SearchService.Combine(
            "Митко", new InterpretedQuery(["Митко"], false), Candidates);

        Assert.Equal("Димитър Стоянов", result.Matches[0].Name);
        Assert.False(result.AiUsed);
        Assert.Null(result.AiNote);
    }

    [Fact]
    public void Combine_WrongAiExactGuess_DoesNotBeatDictionaryOnTie()
    {
        string[] candidates = [.. Candidates, "Радослава Михайлова"];

        var result = SearchService.Combine(
            "Митко", new InterpretedQuery(["Радослава Михайлова"], true), candidates);

        Assert.Equal("Димитър Стоянов", result.Matches[0].Name);
        Assert.False(result.AiUsed);
    }

    [Fact]
    public void Combine_RawAlreadyFindsSameStudent_NoAiCredit()
    {
        var result = SearchService.Combine(
            "Стефчо Колев", new InterpretedQuery(["Стефан Колев"], true), Candidates);

        Assert.Equal("Стефан Колев", result.Matches[0].Name);
        Assert.False(result.AiUsed);
        Assert.Null(result.AiNote);
    }

    [Fact]
    public void Combine_WrongModelGuess_RawMatchStillFound()
    {
        var result = SearchService.Combine(
            "Иван Петров", new InterpretedQuery(["Зорница Кирова"], true), Candidates);

        Assert.Equal("Иван Петров", result.Matches[0].Name);
        Assert.False(result.AiUsed);
    }

    [Fact]
    public void Combine_NoMatches_ReturnsEmptyWithoutAiCredit()
    {
        var result = SearchService.Combine(
            "Зорница Кирова", new InterpretedQuery(["Зорница Кирова"], true), Candidates);

        Assert.Empty(result.Matches);
        Assert.False(result.AiUsed);
        Assert.Null(result.AiNote);
    }

    [Fact]
    public void Combine_SuggestionNeverComesFromModelReading()
    {
        string[] candidates = [.. Candidates, "Тодор Василев"];

        // The model rewrote an unknown name into something close to a real one.
        var result = SearchService.Combine(
            "Зорница Кирова", new InterpretedQuery(["Тодоп Вслв"], true), candidates);

        Assert.Empty(result.Matches);
        Assert.Null(result.DidYouMean);
    }

    [Theory]
    [InlineData("казвам се Мария Иванова")]
    [InlineData("аз съм Мария Иванова")]
    [InlineData("здравей, търся Мария Иванова")]
    public void Combine_SentenceWithoutModel_FindsName(string query)
    {
        // Ollama down or slow: common words around the name must not hide it.
        var result = SearchService.Combine(query, new InterpretedQuery([query], false), Candidates);

        Assert.Equal("Мария Иванова", result.Matches[0].Name);
    }

    [Fact]
    public void Combine_AiNotUsed_IgnoresInterpretedNames()
    {
        var result = SearchService.Combine(
            "Петров", new InterpretedQuery(["Петров"], false), Candidates);

        Assert.Contains(result.Matches, m => m.Name == "Иван Петров");
        Assert.False(result.AiUsed);
    }
}
