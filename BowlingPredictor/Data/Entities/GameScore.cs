namespace BowlingPredictor.Data.Entities;

public class GameScore
{
    public int GameScoreId { get; set; }
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int BowlerId { get; set; }
    public int Scratch { get; set; }
    public bool IsSub { get; set; }

    public Game? Game { get; set; }
    public Team? Team { get; set; }
    public Bowler? Bowler { get; set; }
}

