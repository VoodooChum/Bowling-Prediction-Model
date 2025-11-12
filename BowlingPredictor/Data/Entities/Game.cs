namespace BowlingPredictor.Data.Entities;

public class Game
{
    public int GameId { get; set; }
    public int MatchId { get; set; }
    public int GameNo { get; set; } // 1..3

    public Match? Match { get; set; }
    public ICollection<GameScore> Scores { get; set; } = new List<GameScore>();
}
