namespace BowlingPredictor.Data.Entities;

public class League
{
    public int LeagueId { get; set; }
    public string Name { get; set; } = "";
    public ICollection<Team> Teams { get; set; } = new List<Team>();
    public ICollection<Bowler> Bowlers { get; set; } = new List<Bowler>();
    public ICollection<ModelVersion> ModelVersions { get; set; } = new List<ModelVersion>();
}

