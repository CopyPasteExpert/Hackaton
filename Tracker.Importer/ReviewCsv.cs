using System.Globalization;
using System.Text;

namespace Tracker.Importer;

// import-review.csv: written by `extract`, checked by a person, read by `import`.
// UTF-8 with BOM so Excel shows Cyrillic correctly; quoting per RFC 4180.
public static class ReviewCsv
{
    private const string Header = "status,name,grade,score,place,competition,year,source_url,page";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void Write(string path, IEnumerable<ReviewRow> rows)
    {
        var builder = new StringBuilder(Header).Append("\r\n");

        foreach (var r in rows)
        {
            string[] fields =
            [
                r.Status, r.Name, r.Grade?.ToString(Invariant) ?? "", r.Score?.ToString(Invariant) ?? "",
                r.Place?.ToString(Invariant) ?? "", r.Competition, r.Year.ToString(Invariant),
                r.SourceUrl, r.Page.ToString(Invariant)
            ];

            builder.AppendJoin(',', fields.Select(Quote)).Append("\r\n");
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static List<ReviewRow> Read(string path)
    {
        var records = Parse(File.ReadAllText(path, Encoding.UTF8));

        return records
            .Skip(1)
            .Where(f => f.Count == 9)
            .Select(f => new ReviewRow(
                f[0], f[1], NullableInt(f[2]), NullableDecimal(f[3]), NullableInt(f[4]),
                f[5], int.Parse(f[6], Invariant), f[7], int.Parse(f[8], Invariant)))
            .ToList();
    }

    private static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;

    private static int? NullableInt(string s) => s.Length == 0 ? null : int.Parse(s, Invariant);

    private static decimal? NullableDecimal(string s) => s.Length == 0 ? null : decimal.Parse(s, Invariant);

    private static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                record.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                records.Add(record);
                record = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
