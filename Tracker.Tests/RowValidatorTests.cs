using Tracker.Importer;

namespace Tracker.Tests;

public class RowValidatorTests
{
    // Made-up names in the shape of a MON protocol table.
    private const string Text = """
        1 11 Петър Иванов Колев ПМГ Град 122 7 7 7 250 7 7 7 42 1 6
        2 11 Анна Георгиева Иванова ПМГ Град 129 7 7 7 262 7 7 7 42 1 6
        3 10 Мария-Елена Тодорова Иванова-Петрова СУ Град 146 7 7 7 219 7 7 2 37 3 6
        4 10 Стоян Петров Василев СУ Град 153 7 7 2 247 7 0 7 22 5,75
        5 7 Калина Николаева Димова СУ Град 11,5
        """;

    private static readonly PageText Page = new(1, Text);

    private static readonly ExtractedRow[] PageRows =
    [
        new("Петър Иванов Колев", 11, 42, 1),
        new("Анна Георгиева Иванова", 11, 42, 1),
        new("Мария-Елена Тодорова Иванова-Петрова", 10, 37, 3),
        new("Стоян Петров Василев", 10, 22, null),
        new("Калина Николаева Димова", 7, 11.5m, null)
    ];

    private static ValidatedRow Validate(ExtractedRow row) =>
        RowValidator.Validate(row, Page, PageRows);

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
    public void Validate_NameNotInPage_Rejected()
    {
        var result = Validate(new("Георги Петров Колев", 11, 42, 1));

        Assert.Equal(RowStatus.NameNotInPage, result.Status);
    }

    [Fact]
    public void Validate_LatinName_NameFormat()
    {
        var result = Validate(new("Petar Ivanov", 11, 42, 1));

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
    public void Validate_ScoreFromOtherLine_Rejected()
    {
        var result = Validate(new("Петър Иванов Колев", 11, 37, 1));

        Assert.Equal(RowStatus.ScoreNotOnLine, result.Status);
    }

    [Fact]
    public void Validate_MissingScore_Rejected()
    {
        var result = Validate(new("Петър Иванов Колев", 11, null, 1));

        Assert.Equal(RowStatus.ScoreNotOnLine, result.Status);
    }

    [Fact]
    public void Validate_DecimalCommaScore_Ok()
    {
        var result = Validate(new("Калина Николаева Димова", 7, 11.5m, null));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Equal(11.5m, result.Score);
    }

    [Fact]
    public void Validate_NameSplitAcrossLines_Rejected()
    {
        var page = new PageText(1, """
            1 11 Петър Иванов
            Колев ПМГ Град 122 7 7 7 250 7 7 7 42 1 6
            """);

        var result = RowValidator.Validate(
            new("Петър Иванов Колев", 11, 42, 1), page, [new("Петър Иванов Колев", 11, 42, 1)]);

        Assert.Equal(RowStatus.NameNotInPage, result.Status);
    }

    [Fact]
    public void Validate_FractionalPlace_Dropped()
    {
        // The model read the mark 5,75 as the place.
        var result = Validate(new("Стоян Петров Василев", 10, 22, 5.75m));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Null(result.Place);
    }

    [Fact]
    public void Validate_PlaceBelowHigherScores_Dropped()
    {
        // Three rows on the page have more points, so place 2 is impossible.
        var result = Validate(new("Стоян Петров Василев", 10, 22, 2));

        Assert.Equal(RowStatus.Ok, result.Status);
        Assert.Null(result.Place);
    }

    [Fact]
    public void Validate_PartialLaureateList_KeepsPlace()
    {
        var page = new PageText(1, """
            1 11 Петър Иванов Колев ПМГ Град 42 1
            2 12 Анна Георгиева Иванова ПМГ Град 27 10
            """);
        ExtractedRow[] rows = [new("Петър Иванов Колев", 11, 42, 1), new("Анна Георгиева Иванова", 12, 27, 10)];

        var result = RowValidator.Validate(rows[1], page, rows);

        Assert.Equal(10, result.Place);
    }

    [Fact]
    public void Validate_PlaceRuleIgnoresRowsWithoutScore()
    {
        ExtractedRow[] rows = [new("Анна Георгиева Иванова", 11, null, null), new("Петър Иванов Колев", 11, 42, 1)];

        var result = RowValidator.Validate(rows[1], Page, rows);

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
}
