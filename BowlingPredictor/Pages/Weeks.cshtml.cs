using BowlingPredictor.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

public class WeeksPageModel : PageModel
{
    private readonly LeagueDbContext _db;

    public WeeksPageModel(LeagueDbContext db) => _db = db;

    public string LeagueName { get; set; } = "";
    public List<WeekRow> Weeks { get; set; } = new();

    public async Task OnGetAsync(int leagueId = 1)
    {
        var league = await _db.Leagues.FirstAsync(l => l.LeagueId == leagueId);
        LeagueName = league.Name;

        Weeks = await _db.Matches
            .Where(m => m.LeagueId == leagueId)
            .GroupBy(m => new { m.WeekNo, Date = m.MatchDate.Date })
            .Select(g => new WeekRow
            {
                WeekNo = g.Key.WeekNo,
                Date = g.Key.Date,
                MatchCount = g.Count()
            })
            .OrderBy(w => w.WeekNo)
            .ToListAsync();
    }

    public class WeekRow
    {
        public int WeekNo { get; set; }
        public DateTime Date { get; set; }
        public int MatchCount { get; set; }
    }
}
