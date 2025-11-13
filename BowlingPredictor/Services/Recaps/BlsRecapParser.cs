using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace BowlingPredictor.Services.Recaps;

public class BlsRecapParser : IRecapParser
{
    private static readonly Regex LaneHeaderRegex = new(
       // Example: "Lane 11 - Ain't that Nice  Week 1  8/8/2025"
       //          lane=1, team=1
       //          "Lane 33 - GYHOYA  Week 1  8/8/2025" -> lane=3, team=3
       // General pattern: "Lane <lane><team> - ..."
       @"Lane\s+(\d{1,2})(\d{1,2})\s*-\s*(.*?)\s+Week\s+(\d+)",
       RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
   );




    public Task<ParsedRecap> ParseAsync(Stream pdfStream, CancellationToken ct = default)
    {
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

        // ---- WEEK + DATE ----

        int? weekNo = null;

        var weekHeader = Regex.Match(
            text,
            @"Reprint of Scores\s*[-–—]+\s*Week\s+(\d+)",
            RegexOptions.IgnoreCase
        );

        if (weekHeader.Success)
            weekNo = int.Parse(weekHeader.Groups[1].Value);

        string? dateStr = null;
        var weekDate = Regex.Match(
            text,
            @"Week\s+\d+\s+(\d{1,2}/\d{1,2}/\d{2,4})",
            RegexOptions.IgnoreCase
        );

        if (weekDate.Success)
        {
            dateStr = weekDate.Groups[1].Value;

            if (!weekNo.HasValue)
            {
                var wnMatch = Regex.Match(weekDate.Value, @"Week\s+(\d+)", RegexOptions.IgnoreCase);
                if (wnMatch.Success)
                    weekNo = int.Parse(wnMatch.Groups[1].Value);
            }
        }

        if (dateStr is null)
            throw new InvalidOperationException("Could not find bowling night date in recap PDF.");

        var matchDate = DateTime.ParseExact(
            dateStr,
            new[] { "M/d/yyyy", "MM/dd/yyyy" },
            CultureInfo.InvariantCulture
        );

        // ---- LANE HEADERS ----
        var laneHeaders = LaneHeaderRegex.Matches(text)
    .Cast<Match>()
    .Select(m => new
    {
        Lane = int.Parse(m.Groups[1].Value), // physical lane
        TeamNumber = int.Parse(m.Groups[2].Value), // team #
        TeamName = m.Groups[3].Value.Trim(),
        WeekFound = int.Parse(m.Groups[4].Value)
    })
    .OrderBy(x => x.Lane)
    .ToList();

        Console.WriteLine($"[DBG] Found {laneHeaders.Count} lane headers.");
        foreach (var lm in laneHeaders)
        {
            Console.WriteLine($"[DBG] Lane {lm.Lane} Team #{lm.TeamNumber} Name '{lm.TeamName}' Week {lm.WeekFound}");
        }

        if (laneHeaders.Count == 0)
            throw new InvalidOperationException("No lane headers found in recap PDF.");


        // Build ParsedMatch list
        var parsedMatches = new List<ParsedMatch>();
        for (int i = 0; i + 1 < laneHeaders.Count; i += 2)
        {
            var a = laneHeaders[i];
            var b = laneHeaders[i + 1];

            parsedMatches.Add(new ParsedMatch(
                LaneA: a.Lane,
                LaneB: b.Lane,
                TeamANumber: a.TeamNumber,
                TeamBNumber: b.TeamNumber,
                TeamAName: a.TeamName,
                TeamBName: b.TeamName,
                Games: new List<ParsedGame>() // still empty for now
            ));
        }



        return Task.FromResult(new ParsedRecap(
            MatchDate: matchDate,
            WeekNo: weekNo,
            Matches: parsedMatches
        ));
    }
}
