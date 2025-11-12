using Microsoft.EntityFrameworkCore;
using BowlingPredictor.Data.Entities;

namespace BowlingPredictor.Data;

public class LeagueDbContext : DbContext
{
    public LeagueDbContext(DbContextOptions<LeagueDbContext> options) : base(options) { }

    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Bowler> Bowlers => Set<Bowler>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameScore> GameScores => Set<GameScore>();
    public DbSet<BowlerSeasonAgg> BowlerSeasonAggs => Set<BowlerSeasonAgg>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // League
        b.Entity<League>().HasKey(x => x.LeagueId);

        // Team
        b.Entity<Team>()
            .HasKey(x => x.TeamId);
        b.Entity<Team>()
            .HasOne(x => x.League)
            .WithMany(l => l.Teams)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Team>()
            .HasIndex(x => new { x.LeagueId, x.Number })
            .IsUnique();

        // Bowler
        b.Entity<Bowler>()
            .HasKey(x => x.BowlerId);
        b.Entity<Bowler>()
            .HasOne(x => x.League)
            .WithMany(l => l.Bowlers)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Bowler>()
            .HasOne(x => x.Team)
            .WithMany(t => t.Bowlers)
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.SetNull);
        b.Entity<Bowler>()
            .HasIndex(x => new { x.LeagueId, x.FullName });

        // Match
        b.Entity<Match>()
            .HasKey(x => x.MatchId);
        b.Entity<Match>()
            .HasOne(x => x.League)
            .WithMany()
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Match>()
            .HasOne(x => x.TeamA)
            .WithMany()
            .HasForeignKey(x => x.TeamAId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Match>()
            .HasOne(x => x.TeamB)
            .WithMany()
            .HasForeignKey(x => x.TeamBId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Match>()
            .HasIndex(x => new { x.LeagueId, x.WeekNo, x.TeamAId, x.TeamBId })
            .IsUnique();

        // Game
        b.Entity<Game>()
            .HasKey(x => x.GameId);
        b.Entity<Game>()
            .HasOne(x => x.Match)
            .WithMany(m => m.Games)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Game>()
            .HasIndex(x => new { x.MatchId, x.GameNo })
            .IsUnique();

        // GameScore
        b.Entity<GameScore>()
            .HasKey(x => x.GameScoreId);
        b.Entity<GameScore>()
            .HasOne(x => x.Game)
            .WithMany(g => g.Scores)
            .HasForeignKey(x => x.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<GameScore>()
            .HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<GameScore>()
            .HasOne(x => x.Bowler)
            .WithMany(bw => bw.GameScores)
            .HasForeignKey(x => x.BowlerId)
            .OnDelete(DeleteBehavior.Restrict);
        // Don’t allow duplicate entries for the same bowler in the same game
        b.Entity<GameScore>()
            .HasIndex(x => new { x.GameId, x.BowlerId })
            .IsUnique();

        // BowlerSeasonAgg (composite key)
        b.Entity<BowlerSeasonAgg>()
            .HasKey(x => new { x.LeagueId, x.BowlerId });
        b.Entity<BowlerSeasonAgg>()
            .HasOne(x => x.League)
            .WithMany()
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<BowlerSeasonAgg>()
            .HasOne(x => x.Bowler)
            .WithMany()
            .HasForeignKey(x => x.BowlerId)
            .OnDelete(DeleteBehavior.Restrict);

        // ModelVersion
        b.Entity<ModelVersion>()
            .HasKey(x => x.ModelVersionId);
        b.Entity<ModelVersion>()
            .HasOne(x => x.League)
            .WithMany(l => l.ModelVersions)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);
        // At most one active version per league (optional filtered index)
        b.Entity<ModelVersion>()
            .HasIndex(x => new { x.LeagueId, x.IsActive });
    }
}
