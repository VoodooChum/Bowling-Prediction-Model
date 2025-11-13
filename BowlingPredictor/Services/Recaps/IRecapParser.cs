using System.Text.RegularExpressions;
using BowlingPredictor.Services.Recaps;
using UglyToad.PdfPig;

namespace BowlingPredictor.Services.Recaps;

public interface IRecapParser
{
    Task<ParsedRecap> ParseAsync(Stream pdfStream, CancellationToken ct = default);
}

public class StubRecapParser : IRecapParser
{
    public async Task<ParsedRecap> ParseAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // In a real implementation, use PdfDocument.Open(pdfStream) and walk pages.
        // Here we just pretend and look for a date-like token in raw text.

        pdfStream.Position = 0;
        using var doc = PdfDocument.Open(pdfStream);
        var allText = string.Join("\n", doc.GetPages().Select(p => p.Text));

        // crude date regex – tweak to match your actual header style
        var m = Regex.Match(allText, @"\b(\d{1,2}/\d{1,2}/\d{2,4})\b");
        if (!m.Success)
            throw new InvalidOperationException("Could not find a date in recap PDF.");

        var date = DateTime.Parse(m.Groups[1].Value);

        // crude week number parse (e.g., "Week 1", "Wk 3")
        int? weekNo = null;
        var mw = Regex.Match(allText, @"Week\s+(\d+)", RegexOptions.IgnoreCase);
        if (mw.Success)
            weekNo = int.Parse(mw.Groups[1].Value);

        // TODO: parse actual teams/matches/games here.
        // For now we return an empty matches list so the ingest pipeline still works.
        var recap = new ParsedRecap(date, weekNo, new List<ParsedMatch>());
        return await Task.FromResult(recap);
    }
}

