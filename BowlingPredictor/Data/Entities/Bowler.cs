namespace BowlingPredictor.Data.Entities;

public class Bowler
{
    public int BowlerId { get; set; }
    public int LeagueId { get; set; }
    public string FullName { get; set; } = "";
    public bool IsSub { get; set; }
    public int? TeamId { get; set; } // null if roving sub (Team# = 0 in your sheet)

    public League? League { get; set; }
    public Team? Team { get; set; }
    public ICollection<GameScore> GameScores { get; set; } = new List<GameScore>();
}
