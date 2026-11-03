using Tracker.Importer;

namespace Tracker.Tests;

public class DeduplicatorTests
{
    private const string Math = "Национална олимпиада по математика";
    private const string Url = "https://example.test/math.pdf";

    private static ReviewRow Row(string name, decimal? score, int? place,
        string status = RowStatus.Ok, string competition = Math, int page = 1) =>
        new(status, name, 11, score, place, competition, 2026, Url, page);

    [Fact]
    public void Deduplicate_LaureateAndRanked_KeepsRowWithPlace()
    {
        var result = Deduplicator.Deduplicate(
        [
            Row("Петър Иванов Колев", 42, null, page: 1),
            Row("ПЕТЪР ИВАНОВ КОЛЕВ", 42, 1, page: 2)
        ]);

        var row = Assert.Single(result);
        Assert.Equal(1, row.Place);
    }

    [Fact]
    public void Deduplicate_BothWithoutPlace_KeepsHigherScore()
    {
        var result = Deduplicator.Deduplicate(
        [
            Row("Петър Иванов Колев", 20, null),
            Row("Петър Иванов Колев", 23, null)
        ]);

        Assert.Equal(23m, Assert.Single(result).Score);
    }

    [Fact]
    public void Deduplicate_SameNameDifferentCompetition_KeepsBoth()
    {
        var result = Deduplicator.Deduplicate(
        [
            Row("Петър Иванов Колев", 42, 1),
            Row("Петър Иванов Колев", 30, 4, competition: "Национална олимпиада по физика")
        ]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_NonOkRows_PassThroughUntouched()
    {
        var rejected = Row("Георги Петров Колев", 42, 1, status: RowStatus.NameFormat);
        var unread = Row("", null, null, status: RowStatus.PageNotRead, page: 5);

        var result = Deduplicator.Deduplicate([Row("Петър Иванов Колев", 42, 1), rejected, rejected, unread]);

        Assert.Equal(4, result.Count);
        Assert.Equal(2, result.Count(r => r == rejected));
        Assert.Contains(unread, result);
    }
}
