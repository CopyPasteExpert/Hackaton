using System.Globalization;
using System.Text.RegularExpressions;

namespace Tracker.Importer;

// Checks a row the model extracted against the page it came from, so a made-up
// student or a number taken from the wrong column never reaches the database.
public static partial class RowValidator
{
    [GeneratedRegex(@"^[\p{IsCyrillic}-]+( [\p{IsCyrillic}-]+){1,3}$")]
    private static partial Regex NameFormat();

    public static ValidatedRow Validate(ExtractedRow row, PageText page, IReadOnlyList<ExtractedRow> pageRows)
    {
        var name = row.Name.Trim();
        var grade = row.Grade is >= 1 and <= 12 ? row.Grade : null;

        ValidatedRow Reject(string status) => new(name, grade, row.Score, null, status);

        if (!NameFormat().IsMatch(name))
        {
            return Reject(RowStatus.NameFormat);
        }

        // The model must not invent a student: the full name has to be on the page.
        if (!page.Text.Contains(name))
        {
            return Reject(RowStatus.NameNotInPage);
        }

        if (row.Score is not { } score || !ScoreIsOnNameLine(page.Text, name, score))
        {
            return Reject(RowStatus.ScoreNotOnLine);
        }

        return new ValidatedRow(name, grade, score, CheckPlace(row.Place, score, pageRows), RowStatus.Ok);
    }

    // Protocols are rankings, so totals go down the page. One rise is allowed
    // where a second table starts on the same page. More rises mean the model
    // read another column (a work's fictitious number looks like a plausible
    // score and sits on the same line, so the per-row check cannot catch it).
    public static bool IsRankingOrder(IReadOnlyList<ExtractedRow> rows)
    {
        var scores = rows.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).ToList();
        var rises = scores.Zip(scores.Skip(1)).Count(pair => pair.Second > pair.First);

        return rises <= 1;
    }

    // The score must be one of the words on the same text line as the name.
    private static bool ScoreIsOnNameLine(string text, string name, decimal score)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Contains(name));

        if (line == null)
        {
            return false;
        }

        var dot = score.ToString("0.##", CultureInfo.InvariantCulture);
        var variants = new HashSet<string> { dot, dot.Replace('.', ',') };

        if (score == decimal.Truncate(score))
        {
            variants.Add(dot + ".0");
            variants.Add(dot + ",0");
        }
        else if (dot.Length - dot.IndexOf('.') == 2)
        {
            // 11.5 may be printed as 11,50.
            variants.Add(dot + "0");
            variants.Add(dot.Replace('.', ',') + "0");
        }

        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(variants.Contains);
    }

    // A place is a whole number and cannot be better than the number of students
    // on the page who scored more. This catches a mark (5,75 or 6) read as a place.
    private static int? CheckPlace(decimal? place, decimal score, IReadOnlyList<ExtractedRow> pageRows)
    {
        if (place is not { } value || value != decimal.Truncate(value) || value < 1)
        {
            return null;
        }

        var higher = pageRows.Count(r => r.Score > score);

        return value >= higher + 1 ? (int)value : null;
    }
}
