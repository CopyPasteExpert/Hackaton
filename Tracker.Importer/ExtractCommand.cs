using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tracker.Search;

namespace Tracker.Importer;

// `extract`: PDFs -> model -> validation -> import-review.csv. Never touches the database.
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
        var extractor = new RowExtractor(modelHttp, ollama);

        var watch = Stopwatch.StartNew();
        var rows = new List<ReviewRow>();
        int downloaded = 0, cached = 0, skipped = 0, pagesRead = 0, pagesNotRead = 0;

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

            IReadOnlyList<PageText> pages;

            try
            {
                pages = PdfTextReader.Read(path);
            }
            catch (Exception ex)
            {
                skipped++;
                Console.WriteLine($"  skipped: not a readable PDF ({ex.Message})");
                continue;
            }

            foreach (var page in pages)
            {
                ReviewRow Review(ValidatedRow r) => new(r.Status, r.Name, r.Grade, r.Score, r.Place,
                    source.Competition, source.Year, source.Url, page.Number);

                if (string.IsNullOrWhiteSpace(page.Text))
                {
                    pagesNotRead++;
                    rows.Add(Review(new ValidatedRow("", null, null, null, RowStatus.PageNotRead)));
                    Console.WriteLine($"  page {page.Number}/{pages.Count}: no text (scanned?)");
                    continue;
                }

                IReadOnlyList<ExtractedRow> extracted;

                try
                {
                    extracted = await extractor.ExtractAsync(page.Text);
                }
                catch (Exception ex) when (ex is ExtractionFailedException or TaskCanceledException)
                {
                    pagesNotRead++;
                    rows.Add(Review(new ValidatedRow("", null, null, null, RowStatus.PageNotRead)));
                    Console.WriteLine($"  page {page.Number}/{pages.Count}: not read ({ex.Message})");
                    continue;
                }

                var inOrder = RowValidator.IsRankingOrder(extracted);

                if (!inOrder)
                {
                    // One retry that quotes the wrong numbers back to the model.
                    var wrong = extracted.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).ToList();
                    Console.WriteLine($"  page {page.Number}/{pages.Count}: scores not in ranking order, retrying");

                    try
                    {
                        extracted = await extractor.ExtractAsync(page.Text, wrong);
                        inOrder = RowValidator.IsRankingOrder(extracted);
                    }
                    catch (Exception ex) when (ex is ExtractionFailedException or TaskCanceledException)
                    {
                        // Keep the first attempt; its rows are marked score-order below.
                    }
                }

                pagesRead++;
                var validated = extracted
                    .Select(r => RowValidator.Validate(r, page, extracted))
                    .Select(r => inOrder || r.Status != RowStatus.Ok ? r : r with { Status = RowStatus.ScoreOrder })
                    .ToList();
                rows.AddRange(validated.Select(Review));

                Console.WriteLine($"  page {page.Number}/{pages.Count}: {validated.Count} rows, " +
                    $"{validated.Count(r => r.Status == RowStatus.Ok)} ok");
            }
        }

        var final = Deduplicator.Deduplicate(rows);
        ReviewCsv.Write(reviewPath, final);

        Console.WriteLine();
        Console.WriteLine($"Protocols: {downloaded} downloaded, {cached} from cache, {skipped} skipped");
        Console.WriteLine($"Pages: {pagesRead} read, {pagesNotRead} not read");
        foreach (var group in final.GroupBy(r => r.Status).OrderBy(g => g.Key))
        {
            Console.WriteLine($"Rows {group.Key}: {group.Count()}");
        }
        Console.WriteLine($"Time: {watch.Elapsed:mm\\:ss}");
        Console.WriteLine($"Review: {reviewPath}");

        return 0;
    }
}
