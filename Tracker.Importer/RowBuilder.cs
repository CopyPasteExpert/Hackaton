namespace Tracker.Importer;

// Reads name, grade, score and place from the columns the model named.
// Values are taken as printed, so 18,50 stays 18.50.
public static class RowBuilder
{
    private static readonly Dictionary<string, int> PlaceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["първо"] = 1, ["второ"] = 2, ["трето"] = 3
    };

    // Latin letters that look like Cyrillic ones; protocols sometimes mix them
    // into a name ("Пeшев" with a Latin "e").
    private static readonly Dictionary<char, char> LatinLookalikes = new()
    {
        ['a'] = 'а', ['e'] = 'е', ['o'] = 'о', ['p'] = 'р', ['c'] = 'с', ['x'] = 'х', ['y'] = 'у',
        ['A'] = 'А', ['B'] = 'В', ['E'] = 'Е', ['K'] = 'К', ['M'] = 'М', ['H'] = 'Н', ['O'] = 'О',
        ['P'] = 'Р', ['C'] = 'С', ['T'] = 'Т', ['X'] = 'Х'
    };

    public static List<ExtractedRow> Build(Table table, ColumnMap map) =>
        table.Rows
            .Select(row => new ExtractedRow(
                CleanName(row.Values[map.Name]),
                map.Grade is { } g && int.TryParse(row.Values[g].Trim().TrimEnd('.'), out var grade) ? grade : null,
                ProtocolNumber.Parse(row.Values[map.Score]),
                map.Place is { } p ? Place(row.Values[p]) : null))
            .ToList();

    // A footnote mark ends the name ("Койнова * съгласно т. 4.17."), and
    // Latin look-alike letters inside Cyrillic words become Cyrillic.
    private static string CleanName(string text)
    {
        var star = text.IndexOf('*');
        var name = (star >= 0 ? text[..star] : text).Trim();

        return string.Join(' ', name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Any(c => c is >= 'А' and <= 'я')
                ? new string(word.Select(c => LatinLookalikes.GetValueOrDefault(c, c)).ToArray())
                : word));
    }

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
