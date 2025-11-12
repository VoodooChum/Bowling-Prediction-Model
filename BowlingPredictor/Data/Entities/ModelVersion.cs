namespace BowlingPredictor.Data.Entities;

public class ModelVersion
{
    public int ModelVersionId { get; set; }
    public int LeagueId { get; set; }
    public int TrainedThroughWeek { get; set; }
    public string ParamsJson { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }

    public League? League { get; set; }
}
