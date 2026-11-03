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
                // A page without any header continues the last table. On a page
                // with headers, text before the first one (titles, laureates
                // written as sentences) is not part of a table.
                var isContinuation = segment.Header.Count == 0;

                if (isContinuation && (last == null || segments.Count > 1))
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

            while (header.Count < 4 && i + 1 < lines.Count
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

        foreach (var line in lines)
        {
            line.Words.Sort((a, b) => a.Left.CompareTo(b.Left));
        }

        return lines;
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

    // Each row: its anchor line plus nearby lines without a rank number (a name
    // printed on its own line), split into cells.
    private static List<List<Cell>> BuildRows(List<Line> lines, List<Line> anchors)
    {
        var rowLines = anchors.ToDictionary(a => a, a => new List<Line> { a });

        foreach (var line in lines.Except(anchors))
        {
            var nearest = anchors.MinBy(a => Math.Abs(a.Y - line.Y))!;

            if (Math.Abs(nearest.Y - line.Y) <= JoinDistance)
            {
                rowLines[nearest].Add(line);
            }
        }

        var starts = ColumnStarts(anchors.Select(a => rowLines[a]).ToList());

        return anchors.Select(a => rowLines[a].SelectMany(l => ToCells(l, starts)).ToList()).ToList();
    }

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
