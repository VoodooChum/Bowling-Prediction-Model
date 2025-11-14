using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq; // already added earlier

namespace BowlingPredictor.Services.Recaps
{
    public class RecapIngestService
    {
        private readonly LeagueDbContext _db;
        private readonly IRecapParser _parser;

        public RecapIngestService(LeagueDbContext db, IRecapParser parser)
        {
            _db = db;
            _parser = parser;
        }

        public async Task<RecapIngestResult> IngestAsync(
            Stream pdfStream,
            int leagueId,
            string? sourceFileName = null,
            CancellationToken ct = default)
        {
            var parsed = await _parser.ParseAsync(pdfStream, ct);

            // 1) Deduplicate by league + date
            bool alreadyExists = await _db.Matches
                .AnyAsync(m => m.LeagueId == leagueId &&
                               m.MatchDate.Date == parsed.MatchDate.Date, ct);

            if (alreadyExists)
            {
                return new RecapIngestResult(
                    Skipped: true,
                    Reason: $"Recap for {parsed.MatchDate:yyyy-MM-dd} already ingested.",
                    MatchesCreated: 0,
                    GamesCreated: 0,
                    ScoresCreated: 0
                );
            }

            int weekNo = parsed.WeekNo ?? await GetNextWeekNoAsync(leagueId, ct);

            // 2) Load existing teams for this league keyed by Number
            var teamByNumber = await _db.Teams
                .Where(t => t.LeagueId == leagueId)
                .ToDictionaryAsync(t => t.Number, t => t, ct);

            // 2a) Load all bowlers for this league, keyed by normalized name for quick lookup
            var bowlersByNormalizedName = await _db.Bowlers
                .Where(b => b.LeagueId == leagueId)
                .ToDictionaryAsync(
                    b => NormalizeString(b.FullName),
                    b => b,
                    ct
                );

            static string NormalizeString(string s)
                => s.Trim().ToLowerInvariant();

            static string NormalizeTeamName(string name)
                => NormalizeString(name);

            Team GetTeamOrThrow(int teamNumber, string recapName)
            {
                // Prefer a valid parsed team number
                if (teamNumber != 0 && teamByNumber.TryGetValue(teamNumber, out var byNumber))
                    return byNumber;

                // Fallback: try matching by normalized name
                var norm = NormalizeTeamName(recapName);
                var byName = teamByNumber.Values
                    .FirstOrDefault(t => NormalizeTeamName(t.Name) == norm);

                if (byName != null)
                    return byName;

                throw new InvalidOperationException(
                    $"Team '{recapName}' (parsed number {teamNumber}) was not found in league {leagueId}. " +
                    $"Check that the bowler list import has a team with this name/number."
                );
            }

            Bowler? TryGetBowler(string bowlerName, int teamId)
            {
                var normalized = NormalizeString(bowlerName);

                // Try exact normalized match
                if (bowlersByNormalizedName.TryGetValue(normalized, out var bowler))
                {
                    return bowler;
                }

                // Optional: log when a bowler is not found
                Console.WriteLine(
                    $"[WRN] Bowler '{bowlerName}' (normalized: '{normalized}') not found in league {leagueId}. Skipping score entry."
                );
                return null;
            }

            // 2b) Load existing match keys for this league+week to avoid duplicates
            var existingKeys = await _db.Matches
                .Where(m => m.LeagueId == leagueId && m.WeekNo == weekNo)
                .Select(m => new { m.TeamAId, m.TeamBId })
                .ToListAsync(ct);

            var matchKeySet = new HashSet<(int TeamAId, int TeamBId)>(
                existingKeys.Select(x => (x.TeamAId, x.TeamBId))
            );

            // 3) Create Match rows and track them for game creation
            int matchesCreated = 0;
            var createdMatchPairs = new List<(Match Match, ParsedMatch ParsedMatch)>();

            foreach (var pm in parsed.Matches)
            {
                var teamA = GetTeamOrThrow(pm.TeamANumber, pm.TeamAName);
                var teamB = GetTeamOrThrow(pm.TeamBNumber, pm.TeamBName);

                var key = (teamA.TeamId, teamB.TeamId);

                // Skip duplicate pairs for this league/week
                if (!matchKeySet.Add(key))
                {
                    continue;
                }

                var match = new Match
                {
                    LeagueId = leagueId,
                    WeekNo = weekNo,
                    MatchDate = parsed.MatchDate,
                    TeamAId = teamA.TeamId,
                    TeamBId = teamB.TeamId,
                    IsVerified = true
                };

                _db.Matches.Add(match);
                createdMatchPairs.Add((match, pm));
                matchesCreated++;
            }

            // Save matches first so they have IDs
            await _db.SaveChangesAsync(ct);

            // 4) Create Games and GameScores for each created match
            int gamesCreated = 0;
            int scoresCreated = 0;

            foreach (var (match, parsedMatch) in createdMatchPairs)
            {
                // Load teams from the match
                var teamA = teamByNumber.Values.First(t => t.TeamId == match.TeamAId);
                var teamB = teamByNumber.Values.First(t => t.TeamId == match.TeamBId);

                foreach (var pg in parsedMatch.Games)
                {
                    var game = new Game
                    {
                        MatchId = match.MatchId,
                        GameNo = pg.GameNo
                    };

                    _db.Games.Add(game);
                    gamesCreated++;

                    // Create GameScore entries for each bowler in this game
                    foreach (var pbg in pg.BowlerGames)
                    {
                        var bowler = TryGetBowler(pbg.BowlerName,
                            pbg.TeamName == teamA.Name ? teamA.TeamId : teamB.TeamId);

                        if (bowler is null)
                        {
                            // Bowler not found, skip
                            continue;
                        }

                        var gameScore = new GameScore
                        {
                            Game = game,  // Use relationship instead of explicit GameId
                            BowlerId = bowler.BowlerId,
                            TeamId = bowler.TeamId ?? (pbg.TeamName == teamA.Name ? teamA.TeamId : teamB.TeamId),
                            Scratch = pbg.Scratch,
                            IsSub = bowler.IsSub
                        };

                        _db.GameScores.Add(gameScore);
                        scoresCreated++;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            return new RecapIngestResult(
                Skipped: false,
                Reason: null,
                MatchesCreated: matchesCreated,
                GamesCreated: gamesCreated,
                ScoresCreated: scoresCreated
            );
        }

        private async Task<int> GetNextWeekNoAsync(int leagueId, CancellationToken ct)
        {
            var maxWeek = await _db.Matches
                .Where(m => m.LeagueId == leagueId)
                .Select(m => (int?)m.WeekNo)
                .MaxAsync(ct);

            return (maxWeek ?? 0) + 1;
        }
    }

    public record RecapIngestResult(
        bool Skipped,
        string? Reason,
        int MatchesCreated,
        int GamesCreated,
        int ScoresCreated
    );
}
