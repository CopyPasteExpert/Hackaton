using Tracker.Importer;

namespace Tracker.Tests;

public class RowValidatorTests
{
    // Made-up students of one table, in ranking order.
    private static readonly ExtractedRow[] TableRows =
    [
        new("Петър Иванов Колев", 11, 42, 1),
        new("Анна Георгиева Иванова", 11, 42, 1),
        new("Мария-Елена Тодорова Иванова-Петрова", 10, 37, 3),
        new("Стоян Петров Василев", 10, 22, null),
        new("Калина Николаева Димова", 7, 11.5m, null)
    ];

    private static ValidatedRow Validate(ExtractedRow row) => RowValidator.Validate(row, TableRows);

    [Fact]
    public void Validate_CorrectRow_Ok()
    {
        var result = Validate(new("Петър Иванов Колев", 11, 42, 1));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Equal(1, result.Place);
        Assert.Equal(11, result.Grade);
        Assert.Equal(42m, result.Score);
    }

    [Fact]
    public void Validate_LatinName_NameFormat()
    {
        var result = Validate(new("Petar Ivanov", 11, 42, 1));

        Assert.Equal(RowStatus.NameFormat, result.Status);
    }

    [Fact]
    public void Validate_OneWordName_NameFormat()
    {
        var result = Validate(new("Петър", 11, 42, 1));

        Assert.Equal(RowStatus.NameFormat, result.Status);
    }

    [Fact]
    public void Validate_HyphenatedName_Ok()
    {
        var result = Validate(new("Мария-Елена Тодорова Иванова-Петрова", 10, 37, 3));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Equal(3, result.Place);
    }

    [Fact]
    public void Validate_ScoreNull_ScoreNotNumber()
    {
        var result = Validate(new("Петър Иванов Колев", 11, null, 1));

        Assert.Equal(RowStatus.ScoreNotNumber, result.Status);
    }

    [Fact]
    public void Validate_FractionalPlace_Dropped()
    {
        var result = Validate(new("Стоян Петров Василев", 10, 22, 5.75m));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Null(result.Place);
    }

    [Fact]
    public void Validate_PlaceBelowHigherScores_Dropped()
    {
        // Three rows of the table have more points, so place 2 is impossible.
        var result = Validate(new("Стоян Петров Василев", 10, 22, 2));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Null(result.Place);
    }

    [Fact]
    public void Validate_PartialLaureateList_KeepsPlace()
    {
        ExtractedRow[] rows = [new("Петър Иванов Колев", 11, 42, 1), new("Анна Георгиева Иванова", 12, 27, 10)];

        var result = RowValidator.Validate(rows[1], rows);

        Assert.Equal(10, result.Place);
    }

    [Fact]
    public void Validate_PlaceRuleIgnoresRowsWithoutScore()
    {
        ExtractedRow[] rows = [new("Анна Георгиева Иванова", 11, null, null), new("Петър Иванов Колев", 11, 42, 1)];

        var result = RowValidator.Validate(rows[1], rows);

        Assert.Equal(1, result.Place);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Validate_GradeOutOfRange_Null(int grade)
    {
        var result = Validate(new("Петър Иванов Колев", grade, 42, 1));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Null(result.Grade);
    }

    private static ExtractedRow[] Scores(params int?[] scores) =>
        scores.Select((s, i) => new ExtractedRow($"Ученик Номер{i}", 10, s, null)).ToArray();

    [Fact]
    public void IsRankingOrder_Descending_True()
    {
        Assert.True(RowValidator.IsRankingOrder(Scores(42, 42, 37, 30, 22)));
    }

    [Fact]
    public void IsRankingOrder_OneTableBreak_True()
    {
        Assert.True(RowValidator.IsRankingOrder(Scores(14, 13, 13, 15, 14, 12)));
    }

    [Fact]
    public void IsRankingOrder_FictitiousNumbers_False()
    {
        Assert.False(RowValidator.IsRankingOrder(Scores(250, 262, 219, 247, 268, 257, 223)));
    }

    [Fact]
    public void IsRankingOrder_IgnoresMissingScores()
    {
        Assert.True(RowValidator.IsRankingOrder(Scores(42, null, 37, null, 22)));
    }
}
