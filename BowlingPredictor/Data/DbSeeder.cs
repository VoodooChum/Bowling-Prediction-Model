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

        // NOTE: Teams are NOT auto-seeded to avoid duplicate issues.
        // Teams should be created through the import process or manually via the admin interface.
    }
}