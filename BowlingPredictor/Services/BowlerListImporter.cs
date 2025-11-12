using System.Globalization;
using ClosedXML.Excel;
using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BowlingPredictor.Services;

public class BowlerListImporter
{
    private readonly LeagueDbContext _db;
    public BowlerListImporter(LeagueDbContext db) => _db = db;

    public async Task<ImportResult> ImportAsync(Stream excelStream, int leagueId, CancellationToken ct = default)
    {
        var league = await _db.Leagues.FindAsync(new object?[] { leagueId }, ct)
                     ?? throw new InvalidOperationException($"League {leagueId} not found.");

        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheets.First(); // assuming first sheet

        // Map headers → column indexes
        var headerRow = ws.FirstRowUsed();
        var headers = headerRow.Cells().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber);

        int Col(string name) => headers.TryGetValue(name, out var idx)
            ? idx
            : throw new InvalidOperationException($"Missing expected column: '{name}'");

        // expected columns from your sample:
        var colName = Col("Name");
        var colTeamNum = Col("Team#");
        var colTeamName = headers.ContainsKey("Team") ? Col("Team") : -1;
        var colPins = headers.ContainsKey("Pins") ? Col("Pins") : -1;
        var colGames = headers.ContainsKey("Games") ? Col("Games") : -1;
        var colAvg = headers.ContainsKey("Avg") ? Col("Avg") : -1;
        var colEnteringAvg = headers.ContainsKey("EnteringAvg") ? Col("EnteringAvg") : -1;
        var colGndr = headers.ContainsKey("Gndr") ? Col("Gndr") : -1;

        var createdTeams = 0;
        var updatedTeams = 0;
        var createdBowlers = 0;
        var updatedBowlers = 0;

        // cache teams by (LeagueId, Number) and (LeagueId, Name lower)
        var teamCacheByNum = await _db.Teams.Where(t => t.LeagueId == leagueId).ToDictionaryAsync(t => t.Number, t => t, ct);
        var teamCacheByName = await _db.Teams.Where(t => t.LeagueId == leagueId).ToDictionaryAsync(t => t.Name.ToLowerInvariant(), t => t, ct);

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var name = row.Cell(colName).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var teamNum = SafeInt(row.Cell(colTeamNum).GetString(), row.Cell(colTeamNum).GetValue<int?>());
            var teamName = colTeamName > 0 ? row.Cell(colTeamName).GetString().Trim() : null;

            // ensure team (except 0 = sub pool)
            Team? team = null;
            if (teamNum.HasValue && teamNum.Value > 0)
            {
                if (!teamCacheByNum.TryGetValue(teamNum.Value, out team))
                {
                    // try by name if present
                    if (!string.IsNullOrEmpty(teamName) && teamCacheByName.TryGetValue(teamName.ToLowerInvariant(), out var byName))
                    {
                        team = byName;
                        // if it has no number yet, set it
                        if (team.Number == 0)
                        {
                            team.Number = teamNum.Value;
                            updatedTeams++;
                        }
                    }
                    else
                    {
                        team = new Team
                        {
                            LeagueId = leagueId,
                            Number = teamNum.Value,
                            Name = string.IsNullOrWhiteSpace(teamName) ? $"Team {teamNum.Value}" : teamName
                        };
                        _db.Teams.Add(team);
                        createdTeams++;
                    }

                    // update caches
                    teamCacheByNum[teamNum.Value] = team;
                    teamCacheByName[team.Name.ToLowerInvariant()] = team;
                }
                else
                {
                    // keep name in sync if empty/mismatch and we have a real name
                    if (!string.IsNullOrWhiteSpace(teamName) && !team.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase))
                    {
                        team.Name = teamName!;
                        updatedTeams++;
                        teamCacheByName[team.Name.ToLowerInvariant()] = team;
                    }
                }
            }

            // upsert bowler by (LeagueId + FullName case-insensitive)
            var keyName = name.ToLowerInvariant();
            var bowler = await _db.Bowlers.FirstOrDefaultAsync(b => b.LeagueId == leagueId && b.FullName.ToLower() == keyName, ct);

            bool isSub = !teamNum.HasValue || teamNum.Value == 0;

            if (bowler is null)
            {
                bowler = new Bowler
                {
                    LeagueId = leagueId,
                    FullName = name,
                    IsSub = isSub,
                    // don't set TeamId directly
                    Team = isSub ? null : team
                };
                _db.Bowlers.Add(bowler);
                createdBowlers++;
            }
            else
            {
                bowler.IsSub = isSub;

                if (isSub)
                {
                    bowler.Team = null;
                    bowler.TeamId = null;
                }
                else
                {
                    // point at existing/new team via navigation prop
                    bowler.Team = team;
                    // no need to touch TeamId explicitly; EF will do it
                }

                updatedBowlers++;
            }

            // you can stash extra roster info in a separate table later.
            // (Pins/Games/Avg/EnteringAvg useful for initial BowlerSeasonAgg seeding)
        }

        await _db.SaveChangesAsync(ct);

        return new ImportResult(createdTeams, updatedTeams, createdBowlers, updatedBowlers);
    }

    private static int? SafeInt(string s, int? fallback)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        return fallback;
    }

    public record ImportResult(int TeamsCreated, int TeamsUpdated, int BowlersCreated, int BowlersUpdated);
}
