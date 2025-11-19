using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace BowlingPredictor.Services.Recaps;

public class BlsRecapParser : IRecapParser
{
    private static readonly Regex LaneHeaderRegex = new(
        @"Lane\s+(\d{1,2})(\d{1,2})\s*-\s*(.*?)\s+Week\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );

    // Bowler lines:
    // "Brandon Pretlove bk220 0 223 206 227 656 656"
    // "Pat Brown 122 88 152 116 98 366 630"
    private static readonly Regex BowlerLineRegex = new(
        @"^\s*(?<name>[A-Za-z][A-Za-z' .-]+?)\s+(?:bk)?\d+\s+\d+\s+(\d+)\s+(\d+)\s+(\d+)",
        RegexOptions.Compiled
    );

    // Team Handicap line, e.g.
    // "Handicap 153 153 153 459"
    private static readonly Regex TeamHandicapRegex = new(
        @"^\s*Handicap\s+(\d+)\s+(\d+)\s+(\d+)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // Internal helpers
    private sealed record TeamBowler(string Name, int Game1, int Game2, int Game3);

    private sealed record TeamLaneSection(
        int Lane,
        int TeamNumber,
        string TeamName,
        List<TeamBowler> Bowlers,
        int H1,
        int H2,
        int H3
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

        var laneMatches = LaneHeaderRegex
            .Matches(text)
            .Cast<Match>()
            .ToList();

        Console.WriteLine($"[DBG] Found {laneMatches.Count} lane headers.");

        if (laneMatches.Count == 0)
            throw new InvalidOperationException("No lane headers found in recap PDF.");

        foreach (var m in laneMatches)
        {
            Console.WriteLine(
                $"[DBG] Lane {m.Groups[1].Value} Team #{m.Groups[2].Value} " +
                $"Name '{m.Groups[3].Value.Trim()}' Week {m.Groups[4].Value}"
            );
        }

        // ---- Per-lane sections with bowlers + handicaps ----

        var teamSections = new List<TeamLaneSection>();

        for (int i = 0; i < laneMatches.Count; i++)
        {
            var m = laneMatches[i];

            var lane = int.Parse(m.Groups[1].Value);
            var teamNumber = int.Parse(m.Groups[2].Value);
            var teamName = m.Groups[3].Value.Trim();

            var start = m.Index;
            var end = (i + 1 < laneMatches.Count)
                ? laneMatches[i + 1].Index
                : text.Length;

            var segment = text.Substring(start, end - start);

            var (bowlers, h1, h2, h3) = ParseTeamSegment(segment, lane, teamName);

            teamSections.Add(new TeamLaneSection(
                Lane: lane,
                TeamNumber: teamNumber,
                TeamName: teamName,
                Bowlers: bowlers,
                H1: h1,
                H2: h2,
                H3: h3
            ));
        }

        var orderedTeams = teamSections
            .OrderBy(t => t.Lane)
            .ToList();

        // ---- Build ParsedMatch + ParsedGame ----

        var parsedMatches = new List<ParsedMatch>();

        for (int i = 0; i + 1 < orderedTeams.Count; i += 2)
        {
            var a = orderedTeams[i];
            var b = orderedTeams[i + 1];

            var games = new List<ParsedGame>();

            for (int gameNo = 1; gameNo <= 3; gameNo++)
            {
                var bowlerGames = new List<ParsedBowlerGame>();

                // Team A bowler games and scratch total
                int teamAScratch = 0;
                foreach (var bowler in a.Bowlers)
                {
                    var scratch = gameNo switch
                    {
                        1 => bowler.Game1,
                        2 => bowler.Game2,
                        3 => bowler.Game3,
                        _ => throw new ArgumentOutOfRangeException(nameof(gameNo))
                    };

                    teamAScratch += scratch;

                    bowlerGames.Add(new ParsedBowlerGame(
                        BowlerName: bowler.Name,
                        TeamName: a.TeamName,
                        GameNo: gameNo,
                        Scratch: scratch
                    ));
                }

                // Team B bowler games and scratch total
                int teamBScratch = 0;
                foreach (var bowler in b.Bowlers)
                {
                    var scratch = gameNo switch
                    {
                        1 => bowler.Game1,
                        2 => bowler.Game2,
                        3 => bowler.Game3,
                        _ => throw new ArgumentOutOfRangeException(nameof(gameNo))
                    };

                    teamBScratch += scratch;

                    bowlerGames.Add(new ParsedBowlerGame(
                        BowlerName: bowler.Name,
                        TeamName: b.TeamName,
                        GameNo: gameNo,
                        Scratch: scratch
                    ));
                }

                var teamAHandicap = gameNo switch
                {
                    1 => a.H1,
                    2 => a.H2,
                    3 => a.H3,
                    _ => 0
                };

                var teamBHandicap = gameNo switch
                {
                    1 => b.H1,
                    2 => b.H2,
                    3 => b.H3,
                    _ => 0
                };

                games.Add(new ParsedGame(
                    GameNo: gameNo,
                    TeamAScratch: teamAScratch,
                    TeamAHandicap: teamAHandicap,
                    TeamBScratch: teamBScratch,
                    TeamBHandicap: teamBHandicap,
                    BowlerGames: bowlerGames
                ));
            }

            parsedMatches.Add(new ParsedMatch(
                LaneA: a.Lane,
                LaneB: b.Lane,
                TeamANumber: a.TeamNumber,
                TeamBNumber: b.TeamNumber,
                TeamAName: a.TeamName,
                TeamBName: b.TeamName,
                Games: games
            ));
        }

        return Task.FromResult(new ParsedRecap(
            MatchDate: matchDate,
            WeekNo: weekNo,
            Matches: parsedMatches
        ));
    }

    private static (List<TeamBowler> Bowlers, int H1, int H2, int H3)
        ParseTeamSegment(string segment, int lane, string teamName)
    {
        var bowlers = new List<TeamBowler>();

        // The PDF text is concatenated with no line breaks, so we need to parse it differently.
        // Format: "...NameAvgHDCP-1--2--3-TotalTotalBowlerName1bk###0###...BowlerName2bk###0###...====================ScratchTotal..."

        // Find the bowler section: starts after "NameAvgHDCP" and ends before "===================="
        var headerMatch = Regex.Match(segment, @"NameAvgHDCP", RegexOptions.IgnoreCase);
        var endMatch = Regex.Match(segment, @"={10,}");

        if (!headerMatch.Success || !endMatch.Success)
        {
            Console.WriteLine($"[DBG] Lane {lane} '{teamName}': Could not find bowler section boundaries.");
            return (bowlers, 0, 0, 0);
        }

        var bowlerSectionStart = headerMatch.Index + headerMatch.Length;
        var bowlerSectionEnd = endMatch.Index;

        // The segment contains data for TWO teams side-by-side (left and right columns)
        // Find both "TotalTotal" sequences to determine team boundaries
        var allTotalTotalMatches = Regex.Matches(segment, @"TotalTotal", RegexOptions.IgnoreCase);

        if (allTotalTotalMatches.Count >= 2)
        {
            // There are two teams - extract only the FIRST team's section
            // From: first NameAvgHDCP
            // To: first TotalTotal (exclusive)
            var firstTotalTotalMatch = allTotalTotalMatches[0];
            bowlerSectionEnd = firstTotalTotalMatch.Index;
        }
        else if (allTotalTotalMatches.Count == 1)
        {
            // Only one team in this segment
            bowlerSectionEnd = allTotalTotalMatches[0].Index;
        }

        var bowlerSection = segment.Substring(bowlerSectionStart, Math.Max(0, bowlerSectionEnd - bowlerSectionStart));

        Console.WriteLine($"[DBG] Lane {lane} '{teamName}': Bowler section found, length {bowlerSection.Length}");

        // Parse bowlers from concatenated text using "bk" as the key boundary marker
        // Format: [Name]bk[exactly 3 digit ID][2 digit code/avg][3-digit game 1][3-digit game 2][3-digit game 3]
        // The "bk" prefix always marks the start of a bowler record

        var bowlerPattern = new Regex(
            @"([A-Z][A-Za-z' .-]*?)bk(\d{3})(\d{2})(\d{3})(\d{3})(\d{3})",
            RegexOptions.Compiled
        );

        foreach (Match m in bowlerPattern.Matches(bowlerSection))
        {
            var name = m.Groups[1].Value.Trim();

            // Groups: 1=name, 2=bk-id, 3=code (2 digits), 4=game1, 5=game2, 6=game3
            if (int.TryParse(m.Groups[4].Value, out var game1) &&
                int.TryParse(m.Groups[5].Value, out var game2) &&
                int.TryParse(m.Groups[6].Value, out var game3))
            {
                // Sanity check: bowling scores should be reasonable (0-300)
                if (game1 > 300 || game2 > 300 || game3 > 300)
                {
                    Console.WriteLine($"[DBG] Lane {lane} '{teamName}': REJECTED bowler '{name}': scores {game1}, {game2}, {game3} are out of range (>300)");
                    continue;
                }

                bowlers.Add(new TeamBowler(name, game1, game2, game3));
                Console.WriteLine($"[DBG] Lane {lane} '{teamName}': Found bowler '{name}': G1={game1}, G2={game2}, G3={game3}");
            }
        }

        Console.WriteLine(
            $"[DBG] Lane {lane} '{teamName}': parsed {bowlers.Count} bowlers."
        );

        // Parse handicap line from the full segment
        int h1 = 0, h2 = 0, h3 = 0;
        var hMatch = TeamHandicapRegex.Match(segment);
        if (hMatch.Success)
        {
            h1 = int.Parse(hMatch.Groups[1].Value);
            h2 = int.Parse(hMatch.Groups[2].Value);
            h3 = int.Parse(hMatch.Groups[3].Value);

            Console.WriteLine(
                $"[DBG] Lane {lane} '{teamName}': Handicap H1={h1}, H2={h2}, H3={h3}"
            );
        }
        else
        {
            Console.WriteLine(
                $"[DBG] Lane {lane} '{teamName}': NO Handicap line found, defaulting to 0."
            );
        }

        return (bowlers, h1, h2, h3);
    }
}
