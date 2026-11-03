using Tracker.Search;

namespace Tracker.Tests;

public class NameMatcherTests
{
    private static readonly string[] Candidates =
    [
        "Иван Петров", "Иван Петков", "Мария Иванова", "Георги Димитров",
        "Димитър Стоянов", "Петър Николов", "Тодор Василев", "Анна Георгиева"
    ];

    [Fact]
    public void JaroWinkler_KnownPair()
    {
        Assert.Equal(0.953, NameMatcher.JaroWinkler("марта", "марат"), 3);
    }

    [Fact]
    public void JaroWinkler_Identical_IsOne()
    {
        Assert.Equal(1.0, NameMatcher.JaroWinkler("иван", "иван"));
    }

    [Fact]
    public void JaroWinkler_Empty_IsZero()
    {
        Assert.Equal(0.0, NameMatcher.JaroWinkler("", "иван"));
    }

    [Theory]
    [InlineData("Иван Петров")]
    [InlineData("иван петорв")]
    [InlineData("Ivan Petrov")]
    [InlineData("Петров Иван")]
    public void Match_FindsIvanPetrovFirst(string query)
    {
        var result = NameMatcher.Match(query, Candidates);

        Assert.NotEmpty(result.Matches);
        Assert.Equal("Иван Петров", result.Matches[0].Name);
    }

    [Fact]
    public void Match_Exact_ScoresOne()
    {
        var result = NameMatcher.Match("Иван Петров", Candidates);

        Assert.Equal(1.0, result.Matches[0].Score, 6);
    }

    [Fact]
    public void Match_LastNameOnly_FindsStudent()
    {
        var result = NameMatcher.Match("Петров", Candidates);

        Assert.Contains(result.Matches, m => m.Name == "Иван Петров");
    }

    [Fact]
    public void Match_Unknown_ReturnsNothing()
    {
        var result = NameMatcher.Match("Зорница Кирова", Candidates);

        Assert.Empty(result.Matches);
        Assert.Null(result.DidYouMean);
    }

    [Fact]
    public void Match_CloseButBelowThreshold_SuggestsOrMatches()
    {
        var result = NameMatcher.Match("Тодорр Васил", Candidates);

        if (result.Matches.Count == 0)
        {
            Assert.Equal("Тодор Василев", result.DidYouMean);
        }
        else
        {
            Assert.Equal("Тодор Василев", result.Matches[0].Name);
        }
    }

    [Fact]
    public void Match_TypoInEveryWord_SuggestsWithoutMatching()
    {
        var result = NameMatcher.Match("Тодоп Вслв", Candidates);

        Assert.Empty(result.Matches);
        Assert.Equal("Тодор Василев", result.DidYouMean);
    }

    [Theory]
    [InlineData("Гергана Петрова")]   // only the surname is close to "Иван Петров"
    [InlineData("Зорница Кирова")]
    [InlineData("Митко")]             // closest is only loosely similar
    public void Match_OneWordFarOff_NoSuggestion(string query)
    {
        var result = NameMatcher.Match(query, Candidates);

        Assert.Empty(result.Matches);
        Assert.Null(result.DidYouMean);
    }

    [Fact]
    public void Match_SameFirstNameOtherSurname_NotAMatch()
    {
        // Real data: "Кирова" and "Петрова" share only the "-ова" ending; the
        // exact first name must not carry them over the match threshold.
        string[] candidates = [.. Candidates, "Зорница Цветанова Петрова"];

        var result = NameMatcher.Match("Зорница Кирова", candidates);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Match_MiddleNameMissingInDatabase_Found()
    {
        // Some protocols list only two names; the student types all three.
        string[] candidates = [.. Candidates, "Дарина Костадинова"];

        var result = NameMatcher.Match("Дарина Иванова Костадинова", candidates);

        Assert.Equal("Дарина Костадинова", result.Matches[0].Name);
    }

    [Fact]
    public void Match_FullThreeNameMatch_RanksAboveTwoNameEntry()
    {
        string[] candidates = [.. Candidates, "Иван Колев", "Иван Петров Колев"];

        var result = NameMatcher.Match("Иван Петров Колев", candidates);

        Assert.Equal("Иван Петров Колев", result.Matches[0].Name);
    }

    [Theory]
    [InlineData("Ана Неда Караджова", "Ана-Неда Георгиева Караджова")]
    [InlineData("Асен Христов", "Йоан-Асен Владимиров Христов")]
    public void Match_HyphenatedFirstNameTypedWithSpace_Found(string query, string name)
    {
        var result = NameMatcher.Match(query, [.. Candidates, name]);

        Assert.Equal(name, result.Matches[0].Name);
    }

    [Fact]
    public void Match_CandidateWordCountsOnlyOnce()
    {
        // "иван" and "иванов" must not both be scored against the same "Иван".
        var result = NameMatcher.Match("Иван Иванов", Candidates);

        Assert.DoesNotContain(result.Matches, m => m.Name == "Иван Петров");
        Assert.DoesNotContain(result.Matches, m => m.Name == "Иван Петков");
    }

    [Fact]
    public void Match_SingleLetter_ReturnsNothing()
    {
        var result = NameMatcher.Match("и", Candidates);

        Assert.Empty(result.Matches);
        Assert.Null(result.DidYouMean);
    }
}
