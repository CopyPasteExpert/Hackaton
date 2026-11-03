namespace Tracker.Importer;

// Downloads protocol PDFs one at a time into a local cache. A plain request
// that names itself; no crawling and no pretending to be a browser.
public class ProtocolDownloader(HttpClient http, string cacheDir)
{
    public string CachePath(ProtocolSource source) =>
        Path.Combine(cacheDir, Path.GetFileName(new Uri(source.Url).LocalPath));

    // The local path, or null when the file could not be downloaded.
    public async Task<string?> GetAsync(ProtocolSource source, CancellationToken ct)
    {
        var path = CachePath(source);

        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(cacheDir);

        try
        {
            var bytes = await http.GetByteArrayAsync(source.Url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"  Download failed: {ex.Message}");
            return null;
        }
    }
}
