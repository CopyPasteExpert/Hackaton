using Tracker.Importer;

namespace Tracker.Tests;

public class TableReaderTests
{
    // Positions follow the real protocols: rank @50, grade @75, names from @88,
    // school @240, total @700. Each character is 5 pt wide. Names are made up.
    private static PdfWord W(string text, double left, double y) => new(text, left, left + 5 * text.Length, y);

    private static List<PdfWord> Header(double y = 510) =>
    [
        W("Общ", 690, y + 10), W("бр.", 708, y + 10),
        W("№", 50, y), W("Клас", 65, y), W("Имена", 120, y), W("Училище", 240, y), W("точки", 695, y)
    ];

    private static List<PdfWord> Row(string rank, string grade, string first, string middle, string last,
        string school, string total, double y)
    {
        var x = 88.0;
        var words = new List<PdfWord> { W(rank, 50, y), W(grade, 75, y) };
        foreach (var part in new[] { first, middle, last })
        {
            words.Add(W(part, x, y));
            x += 5 * part.Length + 3;
        }
        words.Add(W(school, 240, y));
        words.Add(W(total, 700, y));
        return words;
    }

    private static List<PdfWord> Page(params IEnumerable<PdfWord>[] parts) => parts.SelectMany(p => p).ToList();

    private static List<PdfWord> FirstPage() => Page(
        Header(),
        Row("1", "11", "Петър", "Иванов", "Колев", "ПМГ", "42", 495),
        Row("2", "10", "Анна", "Георгиева", "Иванова", "СУ", "37", 480),
        Row("3", "9", "Мария", "Тодорова", "Петрова", "ПМГ", "30", 465));

    [Fact]
    public void Read_HeaderAndRows()
    {
        var table = Assert.Single(TableReader.Read([FirstPage()]));

        Assert.Equal(["№", "Клас", "Имена", "Училище", "Общ бр. точки"], table.Columns.Select(c => c.Label));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["1", "11", "Петър Иванов Колев", "ПМГ", "42"], table.Rows[0].Values);
        Assert.Equal(1, table.Page);
    }

    [Fact]
    public void Read_DocumentTitleNotInLabels()
    {
        var page = Page(
            [W("МИНИСТЕРСТВО", 240, 560), W("НА", 305, 560), W("ОБРАЗОВАНИЕТО", 320, 560)],
            FirstPage());

        var table = Assert.Single(TableReader.Read([page]));

        Assert.DoesNotContain(table.Columns, c => c.Label.Contains("МИНИСТЕРСТВО"));
    }

    [Fact]
    public void Read_CenteredHeaderGoesToNearestColumn()
    {
        // The header "Име" sits in the middle of the names column, not at its left edge.
        var page = Page(
            [W("№", 50, 510), W("Име", 125, 510), W("Училище", 240, 510), W("Общо", 700, 510)],
            Row("1", "11", "Петър", "Иванов", "Колев", "ПМГ", "42", 495),
            Row("2", "10", "Анна", "Георгиева", "Иванова", "СУ", "37", 480));

        var table = Assert.Single(TableReader.Read([page]));

        var names = table.Columns.Select((c, i) => (c, i)).Single(x => x.c.Label == "Име").i;
        Assert.Equal("Петър Иванов Колев", table.Rows[0].Values[names]);
    }

    [Fact]
    public void Read_ContinuationPageInheritsColumns()
    {
        var second = Page(
            Row("4", "12", "Георги", "Петров", "Колев", "СУ", "29", 540),
            Row("5", "9", "Елена", "Тодорова", "Иванова", "ПМГ", "28", 525));

        var tables = TableReader.Read([FirstPage(), second]);

        Assert.Equal(2, tables.Count);
        Assert.Equal(tables[0].Columns, tables[1].Columns);
        Assert.Equal(tables[0].HeaderId, tables[1].HeaderId);
        Assert.Equal(2, tables[1].Page);
        Assert.Equal(["4", "12", "Георги Петров Колев", "СУ", "29"], tables[1].Rows[0].Values);
    }

    [Fact]
    public void Read_NewHeaderStartsNewHeaderId()
    {
        var tables = TableReader.Read([FirstPage(), FirstPage()]);

        Assert.NotEqual(tables[0].HeaderId, tables[1].HeaderId);
    }

    [Fact]
    public void Read_WrappedNameJoinsItsRow()
    {
        // A long name is printed on its own line, a few points above the numbers.
        var page = Page(
            Header(),
            Row("1", "11", "Петър", "Иванов", "Колев", "ПМГ", "42", 495),
            [W("Теодор", 88, 482), W("Методиев", 121, 482), W("Лалов", 164, 482)],
            [W("2", 50, 476), W("7", 75, 476), W("СУ", 240, 476), W("18,50", 700, 476)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["2", "7", "Теодор Методиев Лалов", "СУ", "18,50"], table.Rows[1].Values);
    }

    [Fact]
    public void Read_HashAndParticipantHeader()
    {
        // Informatics: "# Логини Участник Кл Училище Общо", logins in the second column.
        var page = Page(
            [W("#", 27, 510), W("Логини", 47, 510), W("Участник", 142, 510), W("Кл", 238, 510), W("Училище", 298, 510), W("Общо", 670, 510)],
            // The second login ends 2 pt before the name, as in the real protocol.
            [W("1", 27, 495), W("A103", 42, 495), W("A203", 62, 495), W("Петър", 84, 495), W("Иванов", 112, 495), W("Колев", 145, 495),
             W("12", 238, 495), W("ПМГ", 298, 495), W("251,99", 669, 495)],
            [W("2", 27, 480), W("A102", 42, 480), W("A202", 62, 480), W("Анна", 84, 480), W("Георгиева", 107, 480), W("Иванова", 155, 480),
             W("11", 238, 480), W("СУ", 298, 480), W("215,90", 669, 480)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(2, table.Rows.Count);
        Assert.Contains("Петър Иванов Колев", table.Rows[0].Values);
        Assert.Contains("251,99", table.Rows[0].Values);
    }

    [Fact]
    public void Read_UnnumberedRows()
    {
        // Bulgarian language: rows have no rank number.
        var page = Page(
            [W("Трите", 105, 510), W("имена", 133, 510), W("Училище", 373, 510), W("Резултат", 697, 510)],
            [W("Анна", 77, 495), W("Евгениева", 100, 495), W("Милева", 148, 495), W("МГ", 373, 495), W("95", 718, 495)],
            [W("Бояна", 77, 480), W("Кирилова", 105, 480), W("Киркова", 148, 480), W("ППМГ", 373, 480), W("91", 718, 480)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(["Анна Евгениева Милева", "МГ", "95"], table.Rows[0].Values);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Read_TextBeforeFirstHeader_Ignored()
    {
        // Laureates listed in sentences above the table, with no score on the line.
        var page = Page(
            [W("1.", 71, 560), W("Петър", 83, 560), W("Маринов", 115, 560), W("–", 160, 560), W("145.", 170, 560), W("ОУ", 195, 560)],
            [W("Трите", 105, 510), W("имена", 133, 510), W("Училище", 373, 510), W("Резултат", 697, 510)],
            [W("Анна", 77, 495), W("Евгениева", 100, 495), W("Милева", 148, 495), W("МГ", 373, 495), W("95", 718, 495)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Single(table.Rows);
        Assert.DoesNotContain(table.Rows, r => r.Values.Any(v => v.Contains("Маринов")));
    }

    [Fact]
    public void Read_TitleRightAboveTwoLineHeader_NotInLabels()
    {
        // Lines 10 pt apart: title, upper header line, main header line.
        var page = Page(
            [W("София,", 300, 530), W("учебна", 338, 530)],
            [W("Общ", 690, 520), W("бр.", 708, 520)],
            [W("№", 50, 510), W("Клас", 65, 510), W("Имена", 120, 510), W("Училище", 240, 510), W("точки", 695, 510)],
            Row("1", "11", "Петър", "Иванов", "Колев", "ПМГ", "42", 495));

        var table = Assert.Single(TableReader.Read([page]));

        Assert.DoesNotContain(table.Columns, c => c.Label.Contains("София") || c.Label.Contains("учебна"));
        Assert.Equal("Общ бр. точки", table.Columns[^1].Label);
    }

    [Fact]
    public void Read_SchoolRightAfterName_SplitAtSharedColumnStart()
    {
        // Math p.5: the school starts at the same x (@200) in every row, and in
        // the first row only 3 pt after the surname.
        var page = Page(
            [W("№", 50, 510), W("Имена", 100, 510), W("Училище", 200, 510), W("Общо", 700, 510)],
            [W("1", 50, 495), W("Ивелин", 88, 495), W("Ивайлов", 121, 495), W("Вълканов", 157, 495), W("СМГ", 200, 495), W("15,0", 700, 495)],
            [W("2", 50, 480), W("Анна", 88, 480), W("Иванова", 111, 480), W("СУ", 200, 480), W("14,0", 700, 480)],
            [W("3", 50, 465), W("Мария", 88, 465), W("Петрова", 116, 465), W("ПМГ", 200, 465), W("12,0", 700, 465)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(["1", "Ивелин Ивайлов Вълканов", "СМГ", "15,0"], table.Rows[0].Values);
    }

    [Fact]
    public void Read_WrappedSurnameBelowJoinsRowAbove()
    {
        // Biology: a long name continues on the line below its row, closer to
        // the next row than to its own.
        var page = Page(
            Header(),
            [W("1", 50, 495), W("11", 75, 495), W("Василена", 88, 495), W("Климентова", 131, 495), W("ПМГ", 240, 495), W("88,8", 700, 495)],
            [W("Кърчанова", 88, 486)],
            Row("2", "10", "Йордан", "Владимиров", "Каменов", "СУ", "88,6", 481));

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal("Василена Климентова Кърчанова", table.Rows[0].Values[2]);
        Assert.Equal("Йордан Владимиров Каменов", table.Rows[1].Values[2]);
    }

    [Fact]
    public void Read_NumbersAndSurnameOnLinesBelowJoinRowAbove()
    {
        var page = Page(
            Header(),
            [W("1", 50, 495), W("11", 75, 495), W("Константин", 88, 495), W("Владимиров", 141, 495), W("ПМГ", 240, 495)],
            [W("88,9", 700, 488)],
            [W("Стоянов", 88, 482)],
            Row("2", "10", "Йордан", "Владимиров", "Каменов", "СУ", "88,6", 470));

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(["1", "11", "Константин Владимиров Стоянов", "ПМГ", "88,9"], table.Rows[0].Values);
    }

    [Fact]
    public void Read_SurnameJustAboveNextRowBaseline()
    {
        // Real biology positions: the wrapped surname sits only 2.3 pt above the
        // next row's baseline, at the same x as the next row's first name.
        var page = Page(
            Header(510),
            [W("1", 50, 495.8), W("11", 75, 495.8), W("Константин", 88, 495.7), W("ПМГ", 240, 495.8), W("88,9", 700, 494.4)],
            [W("Стоянов", 88, 481.3)],
            [W("2", 50, 479.0), W("10", 75, 479.0), W("Василена", 88, 478.9), W("СУ", 240, 479.0), W("88,8", 700, 477.6)],
            [W("Кърчанова", 88, 464.5)],
            [W("3", 50, 462.2), W("9", 75, 462.2), W("Йордан", 88, 462.1), W("СУ", 240, 462.2), W("88,6", 700, 460.8)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(["Константин Стоянов", "Василена Кърчанова", "Йордан"], table.Rows.Select(r => r.Values[2]));
    }

    [Fact]
    public void Read_FootnoteAfterLastRow_Ignored()
    {
        // A footnote under the table runs across the grade and school columns.
        var page = Page(
            FirstPage(),
            [W("*", 88, 457), W("съгласно", 94, 457), W("регламента", 196, 457), W("на", 250, 457), W("олимпиадата", 263, 457)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal(["3", "9", "Мария Тодорова Петрова", "ПМГ", "30"], table.Rows[2].Values);
    }

    [Fact]
    public void Read_TallHeaderWithVerticalLabels_KeepsAllLabelLines()
    {
        // Biology: some labels are printed letter by letter, one per line, so
        // the header is many lines tall; "Общ брой точки" ends near its bottom.
        var page = Page(
            [W("№", 50, 560), W("Имена", 100, 560), W("Общ", 700, 560)],
            [W("а", 400, 550), W("брой", 700, 550)],
            [W("б", 400, 540)],
            [W("в", 400, 530)],
            [W("г", 400, 520)],
            [W("д", 400, 510), W("точки", 700, 510)],
            [W("1", 50, 495), W("Петър", 88, 495), W("Иванов", 116, 495), W("7", 400, 495), W("42", 700, 495)],
            [W("2", 50, 480), W("Анна", 88, 480), W("Иванова", 111, 480), W("5", 400, 480), W("37", 700, 480)]);

        var table = Assert.Single(TableReader.Read([page]));

        Assert.Equal("Общ брой точки", table.Columns[^1].Label);
    }

    [Fact]
    public void Read_PageWithoutTable_NoTable()
    {
        var page = Page(
            [W("Председател", 80, 300), W("на", 140, 300), W("комисията:", 155, 300)],
            [W("Членове:", 80, 280)]);

        Assert.Empty(TableReader.Read([page]));
    }
}
