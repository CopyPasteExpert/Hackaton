using System.Text.RegularExpressions;

namespace Tracker.Importer;

// Rebuilds the ranking tables of a protocol from word positions. MON protocols
// keep each column at the same x on every page, including continuation pages
// that have no header, and sometimes print a long name on its own line above
// the numbers of its row. Plain page text loses both facts; positions keep them.
public static partial class TableReader
{
    private const double LineTolerance = 3;      // words on one text line
    private const double WordGap = 5;            // words of one cell ("Петър Иванов Колев")
    private const double AnchorTolerance = 12;   // rank numbers in one column
    private const double JoinDistance = 9;       // a wrapped name and the line of its row
    private const double HeaderLineGap = 12;     // lines of a multi-line header
    private const int MaxHeaderLines = 20;

    [GeneratedRegex(@"^\d+\.?$")]
    private static partial Regex RankNumber();

    [GeneratedRegex(@"^[\d.,]+$")]
    private static partial Regex Numeric();

    private record Line(double Y, List<PdfWord> Words);

    private record Cell(string Text, double Left, double Right, double Y);

    // Words that start the header line of a ranking table in the protocols seen so far.
    private static readonly string[] HeaderWords = ["Им", "Трите", "Участник", "Ученик"];
    private static readonly string[] HeaderMarks = ["№", "#", "No"];

    private record Segment(List<Line> Header, List<Line> Lines);

    public static List<Table> Read(IReadOnlyList<IReadOnlyList<PdfWord>> pages)
    {
        var tables = new List<Table>();
        (IReadOnlyList<Column> Columns, int Id, bool Ranked)? last = null;
        var headerId = 0;

        for (var i = 0; i < pages.Count; i++)
        {
            var segments = Split(ToLines(pages[i]));

            foreach (var segment in segments)
            {
                // Rows before the first header on a page continue the last
                // table (informatics: a group ends at the top of the page, the
                // next group's header follows). For an unranked table that is
                // only safe on a page without headers: above a header on such a
                // page sit titles and laureates written as sentences, and rows
                // without rank numbers cannot be told apart from them.
                var isContinuation = segment.Header.Count == 0;

                if (isContinuation && (last == null || (segments.Count > 1 && !last.Value.Ranked)))
                {
                    continue;
                }

                var ranked = isContinuation ? last!.Value.Ranked : IsRanked(segment.Lines);
                var anchors = FindAnchors(segment.Lines, ranked);

                if (anchors.Count == 0)
                {
                    continue;
                }

                var rows = BuildRows(segment.Lines, anchors);

                if (!isContinuation)
                {
                    last = (BuildColumns(rows, segment.Header), ++headerId, ranked);
                }

                var columns = last!.Value.Columns;

                var tableRows = rows
                    .Select(cells => new TableRow(columns
                        .Select((_, index) => string.Join(" ", cells
                            .Where(c => NearestColumn(columns, c) == index)
                            .OrderByDescending(c => c.Y).ThenBy(c => c.Left)
                            .Select(c => c.Text)))
                        .ToList()))
                    .ToList();

                tables.Add(new Table(i + 1, last.Value.Id, columns, tableRows));
            }
        }

        return tables;
    }

    // Splits a page at header lines. A header is its keyword line, at most one
    // line right above it and up to two lines right below it without numbers
    // (the wrapped parts of labels such as "Общ бр." / "точки" or "място").
    private static List<Segment> Split(List<Line> lines)
    {
        var segments = new List<Segment> { new([], []) };

        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsHeaderLine(lines[i]))
            {
                segments[^1].Lines.Add(lines[i]);
                continue;
            }

            var header = new List<Line>();
            var current = segments[^1].Lines;

            if (current.Count > 0 && current[^1].Y - lines[i].Y <= HeaderLineGap && !HasNumber(current[^1]))
            {
                header.Add(current[^1]);
                current.RemoveAt(current.Count - 1);
            }

            header.Add(lines[i]);

            // Labels printed letter by letter make some headers many lines tall.
            while (header.Count < MaxHeaderLines && i + 1 < lines.Count
                   && header[^1].Y - lines[i + 1].Y <= HeaderLineGap
                   && !HasNumber(lines[i + 1]) && !IsHeaderLine(lines[i + 1]))
            {
                header.Add(lines[++i]);
            }

            segments.Add(new Segment(header, []));
        }

        return segments.Where(s => s.Header.Count > 0 || s.Lines.Count > 0).ToList();
    }

    private static bool HasNumber(Line line) => ToCells(line).Any(c => Numeric().IsMatch(c.Text));

    private static bool HasLetter(Line line) => line.Words.Any(w => w.Text.Any(char.IsLetter));

    // Most lines with numbers start with a rank number in the same column.
    private static bool IsRanked(List<Line> lines)
    {
        var withNumbers = lines.Count(HasNumber);
        var ranked = lines.Count(l => RankNumber().IsMatch(l.Words[0].Text));
        return ranked > 0 && ranked * 2 >= withNumbers;
    }

    // Ranked tables: lines starting with a rank number in the rank column.
    // Unranked tables: lines with both a number and text.
    private static List<Line> FindAnchors(List<Line> lines, bool ranked)
    {
        if (!ranked)
        {
            return lines.Where(l => HasNumber(l) && HasLetter(l)).ToList();
        }

        var anchorX = FindAnchorX(lines);

        return anchorX == null
            ? []
            : lines.Where(l => RankNumber().IsMatch(l.Words[0].Text)
                               && Math.Abs(l.Words[0].Left - anchorX.Value) < AnchorTolerance).ToList();
    }

    private static List<Line> ToLines(IReadOnlyList<PdfWord> words)
    {
        var lines = new List<Line>();

        foreach (var word in words.OrderByDescending(w => w.Bottom))
        {
            if (lines.Count > 0 && Math.Abs(lines[^1].Y - word.Bottom) < LineTolerance)
            {
                lines[^1].Words.Add(word);
            }
            else
            {
                lines.Add(new Line(word.Bottom, [word]));
            }
        }

        return lines.SelectMany(SplitOverlapping)
            .Select(l => l with { Words = l.Words.OrderBy(w => w.Left).ToList() })
            .ToList();
    }

    // Two words of one text line cannot sit over each other. When they do, the
    // tolerance has caught two lines (a wrapped surname 2.3 pt above the next
    // row's first name): split at the largest gap between baselines.
    private static IEnumerable<Line> SplitOverlapping(Line line)
    {
        var overlap = line.Words.Any(a => line.Words.Any(b =>
            !ReferenceEquals(a, b) && a.Left < b.Right - 0.5 && a.Right > b.Left + 0.5));

        if (!overlap)
        {
            return [line];
        }

        var words = line.Words.OrderByDescending(w => w.Bottom).ToList();
        var split = Enumerable.Range(1, words.Count - 1)
            .MaxBy(i => words[i - 1].Bottom - words[i].Bottom);

        return SplitOverlapping(new Line(words[0].Bottom, words.Take(split).ToList()))
            .Concat(SplitOverlapping(new Line(words[split].Bottom, words.Skip(split).ToList())));
    }

    // The x of the rank column: where most lines that start with a whole number start.
    private static double? FindAnchorX(List<Line> lines)
    {
        var starts = lines
            .Where(l => RankNumber().IsMatch(l.Words[0].Text))
            .Select(l => Math.Round(l.Words[0].Left / 10) * 10)
            .ToList();

        return starts.Count == 0
            ? null
            : starts.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }

    private static bool IsHeaderLine(Line line) =>
        line.Words.Any(w => HeaderMarks.Contains(w.Text)
                            || HeaderWords.Any(h => w.Text.StartsWith(h, StringComparison.OrdinalIgnoreCase)));

    // Each row: its anchor line plus the lines that continue it, split into cells.
    // A line between two rows continues the row above it (biology prints the
    // end of a long name, or the numbers, below the row's first line). The
    // exception is a line just above a row that has nothing in those columns:
    // there the row's name was printed above its numbers (physics).
    private static List<List<Cell>> BuildRows(List<Line> lines, List<Line> anchors)
    {
        const double AfterLastRow = 20;

        var rowLines = anchors.ToDictionary(a => a, a => new List<Line> { a });
        var anchorStarts = ColumnStarts(anchors.Select(a => new List<Line> { a }).ToList());

        // A continuation stays inside the columns; a footnote or a title runs
        // across the start of a column.
        bool CrossesColumn(Line line) => ToCells(line, anchorStarts)
            .Any(c => anchorStarts.Any(x => x > c.Left + 2 && x < c.Right - 2));

        foreach (var line in lines.Except(anchors).Where(l => !CrossesColumn(l)))
        {
            var above = anchors.Where(a => a.Y > line.Y).MinBy(a => a.Y);
            var below = anchors.Where(a => a.Y < line.Y).MaxBy(a => a.Y);

            Line? owner =
                below != null && line.Y - below.Y <= JoinDistance && !Overlaps(line, below) ? below
                : above != null && (below != null || above.Y - line.Y <= AfterLastRow) ? above
                : null;

            if (owner != null)
            {
                rowLines[owner].Add(line);
            }
        }

        var starts = ColumnStarts(anchors.Select(a => rowLines[a]).ToList());

        return anchors.Select(a => rowLines[a].SelectMany(l => ToCells(l, starts)).ToList()).ToList();
    }

    // Whether any word of one line sits over a word of the other.
    private static bool Overlaps(Line a, Line b) =>
        a.Words.Any(x => b.Words.Any(y => x.Left < y.Right + 2 && x.Right > y.Left - 2));

    // x positions where a word starts in most rows: column starts. A school that
    // begins 3 pt after a long surname still begins at the same x as the schools
    // of all other rows, while the ends of names vary.
    private static List<double> ColumnStarts(List<List<Line>> rows)
    {
        const int MinRows = 3;
        const double Share = 0.6;

        if (rows.Count < MinRows)
        {
            return [];
        }

        return rows
            .SelectMany(row => row.SelectMany(l => l.Words).Select(w => Math.Round(w.Left / 2) * 2).Distinct())
            .GroupBy(x => x)
            .Where(g => g.Count() >= Share * rows.Count)
            .Select(g => g.Key)
            .ToList();
    }

    // Neighbouring words form one cell, except: a word with a digit never joins
    // another (the grade sits ~4 pt left of the first name, informatics logins
    // end ~2 pt before it; names never contain digits), and a word at a column
    // start begins a new cell.
    private static List<Cell> ToCells(Line line, IReadOnlyList<double>? columnStarts = null)
    {
        var cells = new List<Cell>();

        foreach (var word in line.Words)
        {
            var atColumnStart = columnStarts?.Any(x => Math.Abs(word.Left - x) <= 2) ?? false;

            if (cells.Count > 0
                && !atColumnStart
                && word.Left - cells[^1].Right <= WordGap
                && !word.Text.Any(char.IsDigit)
                && !cells[^1].Text.Any(char.IsDigit))
            {
                var last = cells[^1];
                cells[^1] = last with { Text = last.Text + " " + word.Text, Right = word.Right };
            }
            else
            {
                cells.Add(new Cell(word.Text, word.Left, word.Right, line.Y));
            }
        }

        return cells;
    }

    // Columns are the x spans covered by the cells of all rows, merged where they
    // overlap. Each header word labels the column nearest to its centre.
    private static List<Column> BuildColumns(List<List<Cell>> rows, List<Line> header)
    {
        var spans = new List<(double Left, double Right)>();

        foreach (var cell in rows.SelectMany(r => r).OrderBy(c => c.Left))
        {
            if (spans.Count > 0 && cell.Left <= spans[^1].Right + 1)
            {
                spans[^1] = (spans[^1].Left, Math.Max(spans[^1].Right, cell.Right));
            }
            else
            {
                spans.Add((cell.Left, cell.Right));
            }
        }

        var unlabelled = spans.Select(s => new Column("", s.Left, s.Right)).ToList();

        var labels = header
            .SelectMany(l => l.Words.Select(w => (Word: w, l.Y)))
            .GroupBy(x => NearestColumn(unlabelled, (x.Word.Left + x.Word.Right) / 2))
            .ToDictionary(g => g.Key, g => string.Join(" ", g
                .OrderByDescending(x => x.Y).ThenBy(x => x.Word.Left)
                .Select(x => x.Word.Text)));

        return unlabelled
            .Select((c, i) => c with { Label = labels.GetValueOrDefault(i, $"Колона {i + 1}") })
            .ToList();
    }

    private static int NearestColumn(IReadOnlyList<Column> columns, Cell cell) =>
        NearestColumn(columns, (cell.Left + cell.Right) / 2);

    private static int NearestColumn(IReadOnlyList<Column> columns, double x) =>
        columns
            .Select((c, i) => (Index: i, Distance: x < c.Left ? c.Left - x : x > c.Right ? x - c.Right : 0))
            .MinBy(t => t.Distance).Index;
}
