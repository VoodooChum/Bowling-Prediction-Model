using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace BowlingPredictor.Services.Recaps;

public class BlsRecapParser : IRecapParser
{
    public Task<ParsedRecap> ParseAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // ensure seekable
        if (!pdfStream.CanSeek)
        {
            var mem = new MemoryStream();
            pdfStream.CopyTo(mem);
            mem.Position = 0;
            pdfStream = mem;
        }

        pdfStream.Position = 0;
        using var doc = PdfDocument.Open(pdfStream);

        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        var text = sb.ToString();

        // -----------------------------
        // 1) Week number
        // -----------------------------

        int? weekNo = null;

        // Prefer the header: "Reprint of Scores -- Week 1"
        var weekHeaderMatch = Regex.Match(
            text,
            @"Reprint of Scores\s*[-–—]+\s*Week\s+(\d+)",
            RegexOptions.IgnoreCase
        );

        if (weekHeaderMatch.Success && int.TryParse(weekHeaderMatch.Groups[1].Value, out var w))
        {
            weekNo = w;
        }

        // -----------------------------
        // 2) Match date
        // -----------------------------

        string? dateStr = null;

        // First, try a lane header style:
        // "Lane 1 1 - Ain't that Nice   Week 1  8/8/2025"
        var laneDateMatch = Regex.Match(
            text,
            @"Lane\s+\d+\s+\d+\s*-\s*.*?Week\s+\d+\s+(\d{1,2}/\d{1,2}/\d{2,4})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (laneDateMatch.Success)
        {
            dateStr = laneDateMatch.Groups[1].Value;
        }
        else
        {
            // Fallback: just grab "Week N  mm/dd/yyyy"
            // This shows up in every recap I checked.
            var weekDateMatch = Regex.Match(
                text,
                @"Week\s+\d+\s+(\d{1,2}/\d{1,2}/\d{2,4})",
                RegexOptions.IgnoreCase
            );

            if (weekDateMatch.Success)
            {
                dateStr = weekDateMatch.Groups[1].Value;

                // If weekNo wasn't found earlier, grab it from this same token.
                if (!weekNo.HasValue)
                {
                    var weekNumMatch = Regex.Match(
                        weekDateMatch.Value,
                        @"Week\s+(\d+)",
                        RegexOptions.IgnoreCase
                    );
                    if (weekNumMatch.Success && int.TryParse(weekNumMatch.Groups[1].Value, out var wn))
                    {
                        weekNo = wn;
                    }
                }
            }
        }

        if (dateStr is null)
        {
            throw new InvalidOperationException(
                "Could not find a bowling night date (pattern 'Week N mm/dd/yyyy') in recap PDF.");
        }

        var matchDate = DateTime.ParseExact(
            dateStr,
            new[] { "M/d/yyyy", "MM/dd/yyyy" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None
        );

        // For now we only return date/week; Matches will be populated later.
        var recap = new ParsedRecap(
            MatchDate: matchDate,
            WeekNo: weekNo,
            Matches: new List<ParsedMatch>()
        );

        return Task.FromResult(recap);
    }
}
