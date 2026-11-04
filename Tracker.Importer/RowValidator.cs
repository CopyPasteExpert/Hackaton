using System.Text.RegularExpressions;

namespace Tracker.Importer;

// Checks a row read from a protocol table before it may reach the database.
// Values come straight from the PDF, so the checks are about shape: a real
// name, a numeric score, and a place that fits the ranking.
public static partial class RowValidator
{
    [GeneratedRegex(@"^[\p{IsCyrillic}-]+( [\p{IsCyrillic}-]+){1,3}$")]
    private static partial Regex NameFormat();

    public static ValidatedRow Validate(ExtractedRow row, IReadOnlyList<ExtractedRow> tableRows)
    {
        var name = row.Name.Trim();
        var grade = row.Grade is >= 1 and <= 12 ? row.Grade : null;

        if (!NameFormat().IsMatch(name))
        {
            return new(name, grade, row.Score, null, RowStatus.NameFormat);
        }

        if (row.Score is not { } score)
        {
            return new(name, grade, null, null, RowStatus.ScoreNotNumber);
        }

        return new(name, grade, score, CheckPlace(row.Place, score, tableRows), RowStatus.Ok);
    }

    // A place is a whole number and cannot be better than the number of students
    // in the table who scored more. This catches a mark (5,75 or 6) read as a place.
    private static int? CheckPlace(decimal? place, decimal score, IReadOnlyList<ExtractedRow> tableRows)
    {
        if (place is not { } value || value != decimal.Truncate(value) || value < 1)
        {
            return null;
        }

        var higher = tableRows.Count(r => r.Score > score);

        return value >= higher + 1 ? (int)value : null;
    }
}
