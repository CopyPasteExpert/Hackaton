using Tracker.Importer;

namespace Tracker.Tests;

public class RowBuilderTests
{
    private static Table TableWith(params string[][] rows) => new(1, 1,
        [new Column("Имена", 88, 200), new Column("Клас", 210, 220), new Column("Общо", 600, 620), new Column("Място", 640, 680)],
        rows.Select(r => new TableRow(r)).ToList());

    private static readonly ColumnMap Map = new(0, 1, 2, 3);

    [Fact]
    public void Build_ReadsMappedColumns()
    {
        var rows = RowBuilder.Build(TableWith(["Петър Иванов Колев", "11", "18,50", "3"]), Map);

        Assert.Equal([new ExtractedRow("Петър Иванов Колев", 11, 18.50m, 3)], rows);
    }

    [Theory]
    [InlineData("първо място", 1)]
    [InlineData("второ място", 2)]
    [InlineData("трето място", 3)]
    [InlineData("Първо", 1)]
    [InlineData("", null)]
    public void Build_PlaceInWords(string place, int? expected)
    {
        var row = Assert.Single(RowBuilder.Build(TableWith(["Петър Иванов Колев", "11", "42", place]), Map));

        Assert.Equal(expected, row.Place);
    }

    [Fact]
    public void Build_ScoreNotNumber_Null()
    {
        var row = Assert.Single(RowBuilder.Build(TableWith(["Петър Иванов Колев", "11", "отсъства", ""]), Map));

        Assert.Null(row.Score);
    }

    [Fact]
    public void Build_NoGradeColumn_NullGrade()
    {
        var row = Assert.Single(RowBuilder.Build(
            TableWith(["Петър Иванов Колев", "11", "42", "1"]), Map with { Grade = null }));

        Assert.Null(row.Grade);
    }
}
