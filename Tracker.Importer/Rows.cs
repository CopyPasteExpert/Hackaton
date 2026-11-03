namespace Tracker.Importer;

public record PageText(int Number, string Text);

// What the model read from a page; nothing here is trusted yet.
public record ExtractedRow(string Name, int? Grade, decimal? Score, decimal? Place);

public record ValidatedRow(string Name, int? Grade, decimal? Score, int? Place, string Status);

public static class RowStatus
{
    public const string Ok = "ok";
    public const string NameNotInPage = "name-not-in-page";
    public const string NameFormat = "name-format";
    public const string ScoreNotOnLine = "score-not-on-line";
    public const string PageNotRead = "page-not-read";
    public const string ScoreOrder = "score-order";
}
