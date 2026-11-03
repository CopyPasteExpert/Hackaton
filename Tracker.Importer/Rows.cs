namespace Tracker.Importer;

// A row read from a protocol table; not checked yet.
public record ExtractedRow(string Name, int? Grade, decimal? Score, decimal? Place);

public record ValidatedRow(string Name, int? Grade, decimal? Score, int? Place, string Status);

public static class RowStatus
{
    public const string Ok = "ok";
    public const string NameFormat = "name-format";
    public const string ScoreNotNumber = "score-not-number";
    public const string PageNotRead = "page-not-read";
    public const string TableNotMapped = "table-not-mapped";
}
