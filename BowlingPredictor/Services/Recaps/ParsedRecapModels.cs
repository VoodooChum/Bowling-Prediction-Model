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
    int TeamAScratch,                // sum of Team A bowler scratch for this game
    int TeamAHandicap,               // Team A handicap applied to this game
    int TeamBScratch,                // sum of Team B bowler scratch for this game
    int TeamBHandicap,               // Team B handicap applied to this game
    List<ParsedBowlerGame> BowlerGames
);

public record ParsedBowlerGame(
    string BowlerName,
    string TeamName,
    int GameNo,
    int Scratch
);
