namespace BowlingPredictor.Data.Entities;

public class Match
{
    public int MatchId { get; set; }
    public int LeagueId { get; set; }
    public int WeekNo { get; set; }
    public DateTime MatchDate { get; set; }
    public int TeamAId { get; set; }
    public int TeamBId { get; set; }
    public bool IsVerified { get; set; }

    public League? League { get; set; }
    public Team? TeamA { get; set; }
    public Team? TeamB { get; set; }
    public ICollection<Game> Games { get; set; } = new List<Game>();
}

