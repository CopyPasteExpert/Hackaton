using UglyToad.PdfPig;

namespace Tracker.Importer;

public static class PdfWordReader
{
    // Words of every page with their positions. MON protocols put an invisible
    // U+FEFF in front of some school names; it is removed here.
    public static IReadOnlyList<IReadOnlyList<PdfWord>> Read(string path)
    {
        using var document = PdfDocument.Open(path);

        return document.GetPages()
            .Select(page => (IReadOnlyList<PdfWord>)page.GetWords()
                .Select(w => new PdfWord(w.Text.Replace("﻿", ""),
                    w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom))
                .Where(w => w.Text.Length > 0)
                .ToList())
            .ToList();
    }
}
