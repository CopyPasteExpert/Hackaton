using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tracker.Search;

namespace Tracker.Importer;

// `extract`: PDFs -> tables by word position -> model names the columns ->
// rows -> validation -> import-review.csv. Never touches the database.
public static class ExtractCommand
{
    public static async Task<int> RunAsync(IConfiguration config, string root, string? protocolsPath = null)
    {
        var importerDir = Path.Combine(root, "Tracker.Importer");
        protocolsPath ??= Path.Combine(importerDir, "protocols.json");
        var reviewPath = Path.Combine(importerDir, "import-review.csv");

        var sources = JsonSerializer.Deserialize<List<ProtocolSource>>(
            await File.ReadAllTextAsync(protocolsPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var ollama = config.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();

        using var downloadHttp = new HttpClient();
        downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Tracker.Importer");
        var downloader = new ProtocolDownloader(downloadHttp, Path.Combine(importerDir, "cache"));

        using var modelHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var mapper = new ColumnMapper(modelHttp, ollama);

        var watch = Stopwatch.StartNew();
        var rows = new List<ReviewRow>();
        int downloaded = 0, cached = 0, skipped = 0;

        foreach (var source in sources)
        {
            Console.WriteLine($"{source.Competition} ({source.Year})");

            var wasCached = File.Exists(downloader.CachePath(source));
            var path = await downloader.GetAsync(source, CancellationToken.None);

            if (path == null)
            {
                skipped++;
                Console.WriteLine("  skipped: could not download");
                continue;
            }

            if (wasCached) cached++; else downloaded++;

            List<Table> tables;

            try
            {
                tables = TableReader.Read(PdfWordReader.Read(path));
            }
            catch (Exception ex)
            {
                skipped++;
                Console.WriteLine($"  skipped: not a readable PDF ({ex.Message})");
                continue;
            }

            if (tables.Count == 0)
            {
                rows.Add(new ReviewRow(RowStatus.PageNotRead, "", null, null, null,
                    source.Competition, source.Year, source.Url, 0));
                Console.WriteLine("  no tables found");
                continue;
            }

            // One model call per header; continuation pages reuse its mapping.
            var maps = new Dictionary<int, ColumnMap?>();

            foreach (var table in tables)
            {
                if (!maps.TryGetValue(table.HeaderId, out var map))
                {
                    map = await mapper.MapAsync(table);
                    maps[table.HeaderId] = map;

                    var described = map == null
                        ? "could not map columns"
                        : $"name=„{table.Columns[map.Name].Label}“, score=„{table.Columns[map.Score].Label}“" +
                          (map.Place is { } p ? $", place=„{table.Columns[p].Label}“" : "") +
                          (map.Grade is { } g ? $", grade=„{table.Columns[g].Label}“" : "");
                    Console.WriteLine($"  table on page {table.Page}: {described}");
                }

                if (map == null)
                {
                    rows.Add(new ReviewRow(RowStatus.TableNotMapped, "", null, null, null,
                        source.Competition, source.Year, source.Url, table.Page));
                    continue;
                }

                var built = RowBuilder.Build(table, map);
                rows.AddRange(built
                    .Select(r => RowValidator.Validate(r, built))
                    .Select(v => new ReviewRow(v.Status, v.Name, v.Grade, v.Score, v.Place,
                        source.Competition, source.Year, source.Url, table.Page)));
            }
        }

        var final = Deduplicator.Deduplicate(rows);
        ReviewCsv.Write(reviewPath, final);

        Console.WriteLine();
        Console.WriteLine($"Protocols: {downloaded} downloaded, {cached} from cache, {skipped} skipped");

        foreach (var group in final.GroupBy(r => r.Competition))
        {
            var ok = group.Count(r => r.Status == RowStatus.Ok);
            var rejected = group.Count(r => r.Status != RowStatus.Ok);
            var share = ok + rejected == 0 ? 0 : 100.0 * rejected / (ok + rejected);
            Console.WriteLine($"{group.Key}: ok {ok}, rejected {rejected} ({share:0}%)");
        }

        foreach (var group in final.Where(r => r.Status != RowStatus.Ok).GroupBy(r => r.Status))
        {
            Console.WriteLine($"Rejected {group.Key}: {group.Count()}");
        }

        Console.WriteLine($"Time: {watch.Elapsed:mm\\:ss}");
        Console.WriteLine($"Review: {reviewPath}");

        return 0;
    }
}
