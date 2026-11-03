namespace Tracker.Importer;

// One line of import-review.csv: a validated row with where it came from.
public record ReviewRow(string Status, string Name, int? Grade, decimal? Score, int? Place,
    string Competition, int Year, string SourceUrl, int Page);
