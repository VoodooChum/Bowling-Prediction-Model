namespace BowlingPredictor.Data.Entities;

public class Team
{
    public int TeamId { get; set; }
    public int LeagueId { get; set; }
    public string Name { get; set; } = "";
    public int Number { get; set; }

    public League? League { get; set; }
    public ICollection<Bowler> Bowlers { get; set; } = new List<Bowler>();
}

