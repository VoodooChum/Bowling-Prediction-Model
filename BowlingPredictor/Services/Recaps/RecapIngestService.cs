using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq; // already added earlier

namespace BowlingPredictor.Services.Recaps
{
    public class RecapIngestService
    {
        private readonly LeagueDbContext _db;
        private readonly IRecapParser _parser;
        private readonly ILogger<RecapIngestService> _logger;

        public RecapIngestService(LeagueDbContext db, IRecapParser parser, ILogger<RecapIngestService> logger)
        {
            _db = db;
            _parser = parser;
            _logger = logger;
        }

        public async Task<RecapIngestResult> IngestAsync(
            Stream pdfStream,
            int leagueId,
            string? sourceFileName = null,
            CancellationToken ct = default)
        {
            var errors = new List<IngestionError>();
            var warnings = new List<IngestionWarning>();

            // Parse PDF with error handling
            ParsedRecap parsed;
            try
            {
                if (pdfStream.CanSeek)
                    pdfStream.Seek(0, SeekOrigin.Begin);

                parsed = await _parser.ParseAsync(pdfStream, ct);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to parse PDF: {Message}", ex.Message);
                errors.Add(new IngestionError(
                    errorCode: "PDF_PARSE_ERROR",
                    message: ex.Message,
                    isCritical: true,
                    innerException: ex
                ));
                return new RecapIngestResult(
                    Skipped: true,
                    Reason: "PDF parsing failed",
                    MatchesCreated: 0,
                    GamesCreated: 0,
                    ScoresCreated: 0,
                    Errors: errors,
                    Warnings: warnings
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while parsing PDF");
                errors.Add(new IngestionError(
                    errorCode: "PDF_READ_ERROR",
                    message: $"Unexpected error reading PDF: {ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
                return new RecapIngestResult(
                    Skipped: true,
                    Reason: "Unexpected error reading PDF",
                    MatchesCreated: 0,
                    GamesCreated: 0,
                    ScoresCreated: 0,
                    Errors: errors,
                    Warnings: warnings
                );
            }

            // 1) Deduplicate by league + date
            bool alreadyExists = await _db.Matches
                .AnyAsync(m => m.LeagueId == leagueId &&
                               m.MatchDate.Date == parsed.MatchDate.Date, ct);

            if (alreadyExists)
            {
                _logger.LogInformation(
                    "Recap for {MatchDate:yyyy-MM-dd} already ingested for league {LeagueId}",
                    parsed.MatchDate, leagueId);

                warnings.Add(new IngestionWarning(
                    code: "DUPLICATE_MATCH",
                    message: $"Recap for {parsed.MatchDate:yyyy-MM-dd} has already been ingested",
                    context: sourceFileName
                ));

                return new RecapIngestResult(
                    Skipped: true,
                    Reason: $"Recap for {parsed.MatchDate:yyyy-MM-dd} already ingested.",
                    MatchesCreated: 0,
                    GamesCreated: 0,
                    ScoresCreated: 0,
                    Errors: errors,
                    Warnings: warnings
                );
            }

            int weekNo = parsed.WeekNo ?? await GetNextWeekNoAsync(leagueId, ct);

            // 2) Load existing teams for this league keyed by Number
            var teamByNumber = await _db.Teams
                .Where(t => t.LeagueId == leagueId)
                .ToDictionaryAsync(t => t.Number, t => t, ct);

            // 2a) Load all bowlers for this league, keyed by normalized name for quick lookup
            var allBowlers = await _db.Bowlers
                .Where(b => b.LeagueId == leagueId)
                .ToListAsync(ct);

            _logger.LogInformation("[BOWLER DEBUG] Loaded {BowlerCount} bowlers from database for league {LeagueId}", allBowlers.Count, leagueId);

            // Create dictionary, but handle duplicates by keeping first occurrence
            var bowlersByNormalizedName = new Dictionary<string, Bowler>();
            foreach (var bowler in allBowlers)
            {
                var normalizedName = NormalizeString(bowler.FullName);
                if (!bowlersByNormalizedName.ContainsKey(normalizedName))
                {
                    bowlersByNormalizedName[normalizedName] = bowler;
                    _logger.LogDebug("[BOWLER DEBUG] Added to index: '{NormalizedName}' -> {BowlerName} (ID: {BowlerId})", normalizedName, bowler.FullName, bowler.BowlerId);
                }
                else
                {
                    _logger.LogWarning(
                        "Duplicate bowler name found: '{BowlerName}' (normalized: '{NormalizedName}'). Using first occurrence (ID: {BowlerId}), skipping duplicate (ID: {DuplicateId})",
                        bowler.FullName, normalizedName, bowlersByNormalizedName[normalizedName].BowlerId, bowler.BowlerId);
                }
            }

            _logger.LogInformation("[BOWLER DEBUG] Bowler index contains {IndexCount} unique normalized names", bowlersByNormalizedName.Count);

            static string NormalizeString(string s)
                => s.Trim().ToLowerInvariant();

            static string NormalizeTeamName(string name)
                => NormalizeString(name);

            Team? TryGetTeam(int teamNumber, string recapName)
            {
                // Prefer a valid parsed team number
                if (teamNumber != 0 && teamByNumber.TryGetValue(teamNumber, out var byNumber))
                    return byNumber;

                // Fallback: try matching by normalized name
                var norm = NormalizeTeamName(recapName);
                var byName = teamByNumber.Values
                    .FirstOrDefault(t => NormalizeTeamName(t.Name) == norm);

                if (byName != null)
                    return byName;

                return null;
            }

            Bowler? TryGetBowler(string bowlerName, int teamId)
            {
                var normalized = NormalizeString(bowlerName);

                _logger.LogDebug("[BOWLER MATCH] Attempting to match bowler: '{BowlerName}' (normalized: '{NormalizedName}')", bowlerName, normalized);

                // Try exact normalized match first
                if (bowlersByNormalizedName.TryGetValue(normalized, out var bowler))
                {
                    _logger.LogDebug("[BOWLER MATCH] ✓ Found exact match for '{BowlerName}' (ID: {BowlerId})", bowlerName, bowler.BowlerId);
                    return bowler;
                }

                _logger.LogDebug("[BOWLER MATCH] ✗ No exact match found for '{NormalizedName}'", normalized);

                // Try name format conversion (First Last vs Last, First)
                var parts = bowlerName.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                _logger.LogDebug("[BOWLER MATCH] Name parts count: {PartsCount}", parts.Length);

                if (parts.Length == 2)
                {
                    // Try swapping the parts: if input is "John Doe", try "Doe, John"
                    // If input is "Doe, John", try "John Doe"
                    string alteredName;
                    if (bowlerName.Contains(","))
                    {
                        // Format: "LastName, FirstName" -> try "FirstName LastName"
                        alteredName = $"{parts[1]} {parts[0]}";
                    }
                    else
                    {
                        // Format: "FirstName LastName" -> try "LastName, FirstName"
                        alteredName = $"{parts[1]}, {parts[0]}";
                    }

                    var alteredNormalized = NormalizeString(alteredName);
                    _logger.LogDebug("[BOWLER MATCH] Trying alternate format: '{AlteredName}' (normalized: '{AlteredNormalized}')", alteredName, alteredNormalized);

                    if (bowlersByNormalizedName.TryGetValue(alteredNormalized, out var alteredBowler))
                    {
                        _logger.LogInformation(
                            "[BOWLER MATCH] ✓ Bowler name format matched: '{OriginalName}' matched as '{AlteredName}' (ID: {BowlerId})",
                            bowlerName, alteredName, alteredBowler.BowlerId);
                        return alteredBowler;
                    }

                    _logger.LogDebug("[BOWLER MATCH] ✗ Alternate format also not found: '{AlteredNormalized}'", alteredNormalized);
                }

                // Log when a bowler is not found (non-critical)
                _logger.LogWarning(
                    "[BOWLER MATCH] ✗ Bowler '{BowlerName}' (normalized: '{NormalizedName}') not found in league {LeagueId}. Skipping score entry.",
                    bowlerName, normalized, leagueId);

                return null;
            }

            // 2b) Load existing match keys for this league+week to avoid duplicates
            var existingKeys = await _db.Matches
                .Where(m => m.LeagueId == leagueId && m.WeekNo == weekNo)
                .Select(m => new { m.TeamAId, m.TeamBId })
                .ToListAsync(ct);

            var matchKeySet = new HashSet<(int TeamAId, int TeamBId)>(
                existingKeys.Select(x => (x.TeamAId, x.TeamBId))
            );

            // 3) Create Match rows and track them for game creation
            int matchesCreated = 0;
            var createdMatchPairs = new List<(Match Match, ParsedMatch ParsedMatch)>();

            foreach (var pm in parsed.Matches)
            {
                var teamA = TryGetTeam(pm.TeamANumber, pm.TeamAName);
                if (teamA is null)
                {
                    _logger.LogError(
                        "Team '{TeamName}' (number {TeamNumber}) not found in league {LeagueId}",
                        pm.TeamAName, pm.TeamANumber, leagueId);

                    errors.Add(new IngestionError(
                        errorCode: "TEAM_NOT_FOUND",
                        message: $"Team '{pm.TeamAName}' (number {pm.TeamANumber}) not found",
                        context: $"Match: {pm.TeamAName} vs {pm.TeamBName}",
                        isCritical: true
                    ));
                    continue;
                }

                var teamB = TryGetTeam(pm.TeamBNumber, pm.TeamBName);
                if (teamB is null)
                {
                    _logger.LogError(
                        "Team '{TeamName}' (number {TeamNumber}) not found in league {LeagueId}",
                        pm.TeamBName, pm.TeamBNumber, leagueId);

                    errors.Add(new IngestionError(
                        errorCode: "TEAM_NOT_FOUND",
                        message: $"Team '{pm.TeamBName}' (number {pm.TeamBNumber}) not found",
                        context: $"Match: {pm.TeamAName} vs {pm.TeamBName}",
                        isCritical: true
                    ));
                    continue;
                }

                var key = (teamA.TeamId, teamB.TeamId);

                // Skip duplicate pairs for this league/week
                if (!matchKeySet.Add(key))
                {
                    _logger.LogInformation(
                        "Duplicate match detected for league {LeagueId}, week {WeekNo}: {TeamA} vs {TeamB}",
                        leagueId, weekNo, pm.TeamAName, pm.TeamBName);

                    warnings.Add(new IngestionWarning(
                        code: "DUPLICATE_MATCH_PAIR",
                        message: $"Match already ingested for this week",
                        context: $"{pm.TeamAName} vs {pm.TeamBName}"
                    ));
                    continue;
                }

                var match = new Match
                {
                    LeagueId = leagueId,
                    WeekNo = weekNo,
                    MatchDate = parsed.MatchDate,
                    TeamAId = teamA.TeamId,
                    TeamBId = teamB.TeamId,
                    IsVerified = true
                };

                _db.Matches.Add(match);
                createdMatchPairs.Add((match, pm));
                matchesCreated++;

                _logger.LogInformation(
                    "Match created for league {LeagueId}, week {WeekNo}: {TeamA} vs {TeamB}",
                    leagueId, weekNo, pm.TeamAName, pm.TeamBName);
            }

            // Save matches first so they have IDs
            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Successfully saved {MatchCount} matches", matchesCreated);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to save matches to database");
                errors.Add(new IngestionError(
                    errorCode: "DATABASE_ERROR",
                    message: $"Failed to save matches: {ex.InnerException?.Message ?? ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
                return new RecapIngestResult(
                    Skipped: matchesCreated == 0,
                    Reason: "Database error",
                    MatchesCreated: matchesCreated,
                    GamesCreated: 0,
                    ScoresCreated: 0,
                    Errors: errors,
                    Warnings: warnings
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error saving matches");
                errors.Add(new IngestionError(
                    errorCode: "UNKNOWN_ERROR",
                    message: $"Unexpected error: {ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
                return new RecapIngestResult(
                    Skipped: matchesCreated == 0,
                    Reason: "Unexpected error",
                    MatchesCreated: matchesCreated,
                    GamesCreated: 0,
                    ScoresCreated: 0,
                    Errors: errors,
                    Warnings: warnings
                );
            }

            // 4) Create Games and GameScores for each created match
            int gamesCreated = 0;
            int scoresCreated = 0;

            foreach (var (match, parsedMatch) in createdMatchPairs)
            {
                // Load teams from the match
                var teamA = teamByNumber.Values.First(t => t.TeamId == match.TeamAId);
                var teamB = teamByNumber.Values.First(t => t.TeamId == match.TeamBId);

                _logger.LogInformation("[GAMESCORE DEBUG] Processing match {MatchId} ({TeamA} vs {TeamB}) with {GameCount} games",
                    match.MatchId, teamA.Name, teamB.Name, parsedMatch.Games.Count);

                foreach (var pg in parsedMatch.Games)
                {
                    var game = new Game
                    {
                        MatchId = match.MatchId,
                        GameNo = pg.GameNo
                    };

                    _db.Games.Add(game);
                    gamesCreated++;

                    _logger.LogInformation("[GAMESCORE DEBUG] Game {GameNo} has {BowlerGameCount} bowler entries", pg.GameNo, pg.BowlerGames.Count);

                    // Create GameScore entries for each bowler in this game
                    foreach (var pbg in pg.BowlerGames)
                    {
                        _logger.LogDebug("[GAMESCORE DEBUG] Processing bowler game: {BowlerName} from {TeamName} - Scratch: {Scratch}",
                            pbg.BowlerName, pbg.TeamName, pbg.Scratch);

                        var bowler = TryGetBowler(pbg.BowlerName,
                            pbg.TeamName == teamA.Name ? teamA.TeamId : teamB.TeamId);

                        if (bowler is null)
                        {
                            // Bowler not found, skip
                            _logger.LogWarning("[GAMESCORE DEBUG] Bowler not found, skipping score for {BowlerName}", pbg.BowlerName);
                            continue;
                        }

                        var gameScore = new GameScore
                        {
                            Game = game,  // Use relationship instead of explicit GameId
                            BowlerId = bowler.BowlerId,
                            TeamId = bowler.TeamId ?? (pbg.TeamName == teamA.Name ? teamA.TeamId : teamB.TeamId),
                            Scratch = pbg.Scratch,
                            IsSub = bowler.IsSub
                        };

                        _db.GameScores.Add(gameScore);
                        scoresCreated++;
                        _logger.LogInformation("[GAMESCORE DEBUG] ✓ Created GameScore for {BowlerName} - Scratch: {Scratch}", pbg.BowlerName, pbg.Scratch);
                    }
                }
            }

            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Successfully saved {GameCount} games and {ScoreCount} scores",
                    gamesCreated, scoresCreated);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to save games and scores to database");
                errors.Add(new IngestionError(
                    errorCode: "DATABASE_ERROR",
                    message: $"Failed to save games/scores: {ex.InnerException?.Message ?? ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error saving games and scores");
                errors.Add(new IngestionError(
                    errorCode: "UNKNOWN_ERROR",
                    message: $"Unexpected error: {ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
            }

            _logger.LogInformation(
                "Recap ingestion completed for league {LeagueId}: {MatchCount} matches, {GameCount} games, {ScoreCount} scores, {ErrorCount} errors, {WarningCount} warnings",
                leagueId, matchesCreated, gamesCreated, scoresCreated, errors.Count, warnings.Count);

            return new RecapIngestResult(
                Skipped: false,
                Reason: null,
                MatchesCreated: matchesCreated,
                GamesCreated: gamesCreated,
                ScoresCreated: scoresCreated,
                Errors: errors,
                Warnings: warnings
            );
        }

        private async Task<int> GetNextWeekNoAsync(int leagueId, CancellationToken ct)
        {
            var maxWeek = await _db.Matches
                .Where(m => m.LeagueId == leagueId)
                .Select(m => (int?)m.WeekNo)
                .MaxAsync(ct);

            return (maxWeek ?? 0) + 1;
        }
    }

    public record RecapIngestResult(
        bool Skipped,
        string? Reason,
        int MatchesCreated,
        int GamesCreated,
        int ScoresCreated,
        List<IngestionError>? Errors = null,
        List<IngestionWarning>? Warnings = null
    )
    {
        public RecapIngestResult(
            bool skipped,
            string? reason,
            int matchesCreated,
            int gamesCreated,
            int scoresCreated)
            : this(skipped, reason, matchesCreated, gamesCreated, scoresCreated, new(), new())
        {
        }

        public bool HasErrors => Errors?.Any(e => e.IsCritical) ?? false;
        public bool HasWarnings => Warnings?.Count > 0;
        public bool IsPartialSuccess => GamesCreated > 0 && Errors?.Any(e => !e.IsCritical) == true;
    };
}
