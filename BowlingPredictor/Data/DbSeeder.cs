// Data/DbSeeder.cs
using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static async Task SeedAsync(LeagueDbContext db)
    {
        // Ensure Friday league exists
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Name == "Friday Night");
        if (league is null)
        {
            league = new League { Name = "Friday Night" };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
        }

        // Ensure Teams exist for that league
        bool teamsExist = await db.Teams.AnyAsync(t => t.LeagueId == league.LeagueId);
        if (!teamsExist)
        {
            db.Teams.AddRange(
                new Team { LeagueId = league.LeagueId, Name = "Ain't that Nice", Number = 1 },
                new Team { LeagueId = league.LeagueId, Name = "Westbank Lawnmower", Number = 2 }
                // add more if you want now
            );
            await db.SaveChangesAsync();
        }
    }
}