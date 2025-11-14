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

        var lines = segment.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        var inBowlerSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (!inBowlerSection)
            {
                if (line.Contains("Name Avg HDCP", StringComparison.OrdinalIgnoreCase))
                {
                    inBowlerSection = true;
                }
                continue;
            }

            // End of bowler section on separators / totals
            if (line.Contains("====") ||
                line.StartsWith("Scratch Total", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Handicap", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            {
                // Don't break on Handicap here, we parse it from full segment below.
                if (!line.StartsWith("Handicap", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            var m = BowlerLineRegex.Match(line);
            if (!m.Success)
                continue;

            var name = m.Groups["name"].Value.Trim();
            var game1 = int.Parse(m.Groups[2].Value);
            var game2 = int.Parse(m.Groups[3].Value);
            var game3 = int.Parse(m.Groups[4].Value);

            bowlers.Add(new TeamBowler(name, game1, game2, game3));
        }

        Console.WriteLine(
            $"[DBG] Lane {lane} '{teamName}': parsed {bowlers.Count} bowler lines."
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
