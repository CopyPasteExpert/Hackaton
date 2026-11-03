namespace Tracker.Importer;

// A word on a PDF page. Bottom grows upwards (PDF coordinates).
public record PdfWord(string Text, double Left, double Right, double Bottom);

public record Column(string Label, double Left, double Right);

// One value per column, "" when the row has nothing there.
public record TableRow(IReadOnlyList<string> Values);

// HeaderId is shared by a table and its continuation pages, which have no header
// of their own and reuse its columns.
public record Table(int Page, int HeaderId, IReadOnlyList<Column> Columns, IReadOnlyList<TableRow> Rows);

// Indexes into Table.Columns.
public record ColumnMap(int Name, int? Grade, int Score, int? Place);
