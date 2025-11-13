namespace BowlingPredictor.Services.Recaps;

public record ParsedRecap(
    DateTime MatchDate,
    int? WeekNo,
    List<ParsedMatch> Matches
);

public record ParsedMatch(
    string LaneLabel,      // e.g. "Lane 1-2" or "1-2"
    string TeamAName,
    string TeamBName,
    List<ParsedGame> Games // usually 3 games
);

public record ParsedGame(
    int GameNo,            // 1, 2, 3
    List<ParsedBowlerGame> BowlerGames
);

public record ParsedBowlerGame(
    string BowlerName,
    string TeamName,
    int GameScore
);
