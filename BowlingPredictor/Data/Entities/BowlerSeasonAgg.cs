namespace BowlingPredictor.Data.Entities;

public class BowlerSeasonAgg
{
    // Composite key: (LeagueId, BowlerId)
    public int LeagueId { get; set; }
    public int BowlerId { get; set; }
    public double Mean { get; set; }
    public double Sd { get; set; }
    public int VerifiedGames { get; set; }
    public double Reliability { get; set; }

    public League? League { get; set; }
    public Bowler? Bowler { get; set; }
}

