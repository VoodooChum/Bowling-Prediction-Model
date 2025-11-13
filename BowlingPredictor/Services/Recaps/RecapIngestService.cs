using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BowlingPredictor.Services.Recaps;

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
        // 1) Parse the PDF into a ParsedRecap (date + week)
        var parsed = await _parser.ParseAsync(pdfStream, ct);

        // 2) Duplicate protection: same League + same date
        bool alreadyExists = await _db.Matches
            .AnyAsync(m => m.LeagueId == leagueId &&
                           m.MatchDate.Date == parsed.MatchDate.Date,
                      ct);

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

        // 3) For now, we store ONE "placeholder" Match per recap date.
        //    This gives us a concrete row to dedupe against, and we can
        //    later replace this with real lane/team parsing.

        // We need valid TeamAId/TeamBId to satisfy FK, so grab the first 2 teams.
        var teamIds = await _db.Teams
            .Where(t => t.LeagueId == leagueId)
            .OrderBy(t => t.Number)
            .Select(t => t.TeamId)
            .Take(2)
            .ToListAsync(ct);

        if (teamIds.Count < 2)
        {
            throw new InvalidOperationException(
                "Need at least 2 teams in this league before importing recaps. " +
                "Make sure you imported the Bowler List / created teams first.");
        }

        var match = new Match
        {
            LeagueId = leagueId,
            WeekNo = parsed.WeekNo ?? await GetNextWeekNoAsync(leagueId, ct),
            MatchDate = parsed.MatchDate,
            TeamAId = teamIds[0],
            TeamBId = teamIds[1],
            IsVerified = true   // these are official recap results
        };

        _db.Matches.Add(match);
        await _db.SaveChangesAsync(ct);

        return new RecapIngestResult(
            Skipped: false,
            Reason: null,
            MatchesCreated: 1,
            GamesCreated: 0,
            ScoresCreated: 0
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
