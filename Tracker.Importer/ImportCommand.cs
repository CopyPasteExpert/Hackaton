using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Tracker.Importer;

// `import`: writes the reviewed rows (status ok) of import-review.csv into
// competition_results, replacing what is there, in one transaction.
public static class ImportCommand
{
    public static async Task<int> RunAsync(IConfiguration config, string root)
    {
        var reviewPath = Path.Combine(root, "Tracker.Importer", "import-review.csv");

        if (!File.Exists(reviewPath))
        {
            Console.Error.WriteLine($"No review file at {reviewPath}. Run `extract` first.");
            return 1;
        }

        var rows = ReviewCsv.Read(reviewPath).Where(r => r.Status == RowStatus.Ok).ToList();

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("The review file has no rows with status ok; nothing was imported.");
            return 1;
        }

        await using var connection = new MySqlConnection(config.GetConnectionString("Tracker"));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Saved results point at ids that are about to disappear.
        foreach (var table in new[] { "saved_results", "competition_results" })
        {
            await using var delete = new MySqlCommand($"DELETE FROM {table}", connection, transaction);
            await delete.ExecuteNonQueryAsync();
        }

        await using var insert = new MySqlCommand("""
            INSERT INTO competition_results
                (participant_name, competition_name, competition_year, score, grade, place, source_url)
            VALUES (@name, @competition, @year, @score, @grade, @place, @url)
            """, connection, transaction);

        var name = insert.Parameters.Add("@name", MySqlDbType.VarChar);
        var competition = insert.Parameters.Add("@competition", MySqlDbType.VarChar);
        var year = insert.Parameters.Add("@year", MySqlDbType.Int32);
        var score = insert.Parameters.Add("@score", MySqlDbType.Decimal);
        var grade = insert.Parameters.Add("@grade", MySqlDbType.Int32);
        var place = insert.Parameters.Add("@place", MySqlDbType.Int32);
        var url = insert.Parameters.Add("@url", MySqlDbType.VarChar);

        foreach (var row in rows)
        {
            name.Value = row.Name;
            competition.Value = row.Competition;
            year.Value = row.Year;
            score.Value = (object?)row.Score ?? DBNull.Value;
            grade.Value = (object?)row.Grade ?? DBNull.Value;
            place.Value = (object?)row.Place ?? DBNull.Value;
            url.Value = row.SourceUrl;
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        Console.WriteLine($"Imported {rows.Count} results for {rows.Select(r => r.Name).Distinct().Count()} students.");
        return 0;
    }
}
