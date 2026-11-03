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
        var result = SearchService.Combine(
            "Митко", new InterpretedQuery(["Димитър"], true), Candidates);

        Assert.Equal("Димитър Стоянов", result.Matches[0].Name);
        Assert.True(result.AiUsed);
        Assert.Equal("Разбрах „Митко“ като „Димитър Стоянов“", result.AiNote);
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
    public void Combine_AiNotUsed_IgnoresInterpretedNames()
    {
        var result = SearchService.Combine(
            "Петров", new InterpretedQuery(["Петров"], false), Candidates);

        Assert.Contains(result.Matches, m => m.Name == "Иван Петров");
        Assert.False(result.AiUsed);
    }
}
