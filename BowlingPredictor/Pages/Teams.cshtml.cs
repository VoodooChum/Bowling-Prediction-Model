using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

public class TeamsPageModel : PageModel
{
    private readonly LeagueDbContext _db;
    public League League { get; set; } = default!;
    public List<Team> Teams { get; set; } = new();
    public List<Bowler> Subs { get; set; } = new();

    public TeamsPageModel(LeagueDbContext db) => _db = db;

    public async Task OnGet()
    {
        // For now, just grab the first league (Friday Night)
        League = await _db.Leagues.FirstAsync();

        Teams = await _db.Teams
            .Where(t => t.LeagueId == League.LeagueId)
            .Include(t => t.Bowlers)
            .ToListAsync();

        Subs = await _db.Bowlers
            .Where(b => b.LeagueId == League.LeagueId && b.IsSub)
            .ToListAsync();
    }
}

