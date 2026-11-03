using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Tracker.Importer;

public static class PdfTextReader
{
    // Text of every page in reading order. MON protocols put an invisible
    // U+FEFF in front of some school names; it is removed here.
    public static IReadOnlyList<PageText> Read(string path)
    {
        using var document = PdfDocument.Open(path);

        return document.GetPages()
            .Select(page => new PageText(
                page.Number,
                ContentOrderTextExtractor.GetText(page).Replace("﻿", "")))
            .ToList();
    }
}
