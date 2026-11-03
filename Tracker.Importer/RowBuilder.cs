namespace Tracker.Importer;

// Reads name, grade, score and place from the columns the model named.
// Values are taken as printed, so 18,50 stays 18.50.
public static class RowBuilder
{
    private static readonly Dictionary<string, int> PlaceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["първо"] = 1, ["второ"] = 2, ["трето"] = 3
    };

    public static List<ExtractedRow> Build(Table table, ColumnMap map) =>
        table.Rows
            .Select(row => new ExtractedRow(
                row.Values[map.Name].Trim(),
                map.Grade is { } g && int.TryParse(row.Values[g].Trim().TrimEnd('.'), out var grade) ? grade : null,
                ProtocolNumber.Parse(row.Values[map.Score]),
                map.Place is { } p ? Place(row.Values[p]) : null))
            .ToList();

    // "1", "1." or "първо място".
    private static decimal? Place(string text)
    {
        var first = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (first == null)
        {
            return null;
        }

        return PlaceWords.TryGetValue(first, out var place) ? place : ProtocolNumber.Parse(first.TrimEnd('.'));
    }
}
