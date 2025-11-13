namespace BowlingPredictor.Services.Recaps;

public record ParsedRecap(
    DateTime MatchDate,
    int? WeekNo,
    List<ParsedMatch> Matches
);

public record ParsedMatch(
    int LaneA,
    int LaneB,
    int TeamANumber,
    int TeamBNumber,
    string TeamAName,
    string TeamBName,
    List<ParsedGame> Games
);

public record ParsedGame(
    int GameNo,                      // 1..3
    List<ParsedBowlerGame> BowlerGames
);

public record ParsedBowlerGame(
    string BowlerName,
    string TeamName,
    int GameNo,
    int Scratch
);
