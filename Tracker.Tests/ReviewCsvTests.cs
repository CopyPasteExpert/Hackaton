using Tracker.Importer;

namespace Tracker.Tests;

public class ReviewCsvTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"review-{Guid.NewGuid():N}.csv");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void ReviewCsv_RoundTrip()
    {
        ReviewRow[] rows =
        [
            new(RowStatus.Ok, "Петър Иванов Колев", 11, 42, 1,
                "Олимпиада по химия, „опазване“ и \"среда\"", 2026, "https://example.test/a.pdf", 2),
            new(RowStatus.Ok, "Калина Николаева Димова", null, 11.5m, null,
                "Олимпиада по математика", 2026, "https://example.test/a.pdf", 5),
            new(RowStatus.PageNotRead, "", null, null, null,
                "Олимпиада по физика", 2026, "https://example.test/b.pdf", 3)
        ];

        ReviewCsv.Write(_path, rows);

        Assert.Equal(rows, ReviewCsv.Read(_path));
    }

    [Fact]
    public void ReviewCsv_WritesUtf8Bom()
    {
        ReviewCsv.Write(_path, []);

        var bytes = File.ReadAllBytes(_path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
    }

    [Fact]
    public void ReviewCsv_HasHeader()
    {
        ReviewCsv.Write(_path, []);

        Assert.Equal("status,name,grade,score,place,competition,year,source_url,page",
            File.ReadAllLines(_path)[0]);
    }
}
