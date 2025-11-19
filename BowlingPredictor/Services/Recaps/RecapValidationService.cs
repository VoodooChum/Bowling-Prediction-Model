using BowlingPredictor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BowlingPredictor.Services.Recaps;

/// <summary>
/// Validates a PDF recap file before attempting to ingest it.
/// </summary>
public class RecapValidationService
{
    private readonly LeagueDbContext _db;
    private readonly IRecapParser _parser;
    private readonly ILogger<RecapValidationService> _logger;

    public RecapValidationService(LeagueDbContext db, IRecapParser parser, ILogger<RecapValidationService> logger)
    {
        _db = db;
        _parser = parser;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateBeforeIngestAsync(
        Stream pdfStream,
        int leagueId,
        CancellationToken ct = default)
    {
        var errors = new List<IngestionError>();
        var warnings = new List<IngestionWarning>();

        try
        {
            // 1. Try to parse the PDF
            ParsedRecap parsed;
            try
            {
                // Reset stream position
                if (pdfStream.CanSeek)
                {
                    pdfStream.Seek(0, SeekOrigin.Begin);
                }

                parsed = await _parser.ParseAsync(pdfStream, ct);
            }
            catch (Exception ex)
            {
                errors.Add(new IngestionError(
                    errorCode: "PDF_PARSE_ERROR",
                    message: $"Failed to parse PDF: {ex.Message}",
                    isCritical: true,
                    innerException: ex
                ));
                return new ValidationResult(errors, warnings);
            }

            // 2. Check if date was extracted
            if (parsed.MatchDate == DateTime.MinValue)
            {
                var error = new IngestionError(
                    errorCode: "NO_DATE_FOUND",
                    message: "Could not extract match date from PDF",
                    isCritical: true
                );
                errors.Add(error);
                _logger.LogError("Validation error - NO_DATE_FOUND: {Message}", error.Message);
            }
            else
            {
                _logger.LogInformation("Validation - Date extracted: {MatchDate:yyyy-MM-dd}", parsed.MatchDate);
            }

            // 3. Check if matches were found
            if (!parsed.Matches.Any())
            {
                var error = new IngestionError(
                    errorCode: "NO_MATCHES_FOUND",
                    message: "No matches found in PDF",
                    isCritical: true
                );
                errors.Add(error);
                _logger.LogError("Validation error - NO_MATCHES_FOUND: {Message}", error.Message);
            }
            else
            {
                _logger.LogInformation("Validation - {MatchCount} matches found in PDF", parsed.Matches.Count);
            }

            // 4. Check if already ingested
            bool alreadyExists = await _db.Matches
                .AnyAsync(m => m.LeagueId == leagueId &&
                               m.MatchDate.Date == parsed.MatchDate.Date, ct);

            if (alreadyExists)
            {
                warnings.Add(new IngestionWarning(
                    code: "DUPLICATE_MATCH",
                    message: $"Recap for {parsed.MatchDate:yyyy-MM-dd} has already been ingested",
                    context: "This file will be skipped"
                ));
            }

            // 5. Load existing teams and bowlers for validation
            var teamByNumber = await _db.Teams
                .Where(t => t.LeagueId == leagueId)
                .ToDictionaryAsync(t => t.Number, t => t, ct);

            _logger.LogInformation("Validation - Found {TeamCount} teams in league {LeagueId}", teamByNumber.Count, leagueId);
            foreach (var team in teamByNumber.Values)
            {
                _logger.LogDebug("  Team: #{TeamNumber} - {TeamName}", team.Number, team.Name);
            }

            var allBowlers = await _db.Bowlers
                .Where(b => b.LeagueId == leagueId)
                .ToListAsync(ct);

            // Create dictionary, but handle duplicates by keeping first occurrence
            var bowlersByNormalizedName = new Dictionary<string, Data.Entities.Bowler>();
            var duplicateBowlers = new List<(string normalizedName, int count)>();

            foreach (var bowler in allBowlers)
            {
                var normalizedName = NormalizeString(bowler.FullName);
                if (!bowlersByNormalizedName.ContainsKey(normalizedName))
                {
                    bowlersByNormalizedName[normalizedName] = bowler;
                }
                else
                {
                    _logger.LogWarning("Duplicate bowler name found: '{BowlerName}' (normalized: '{NormalizedName}'). Using first occurrence (ID: {BowlerId}), skipping duplicate (ID: {DuplicateId})",
                        bowler.FullName, normalizedName, bowlersByNormalizedName[normalizedName].BowlerId, bowler.BowlerId);
                }
            }

            _logger.LogInformation("Validation - Found {BowlerCount} unique bowlers in league {LeagueId} ({TotalCount} total, {DuplicateCount} duplicates)",
                bowlersByNormalizedName.Count, leagueId, allBowlers.Count, allBowlers.Count - bowlersByNormalizedName.Count);

            // 6. Validate teams exist
            _logger.LogInformation("Validation - PDF contains {MatchCount} matches", parsed.Matches.Count);
            foreach (var match in parsed.Matches)
            {
                _logger.LogInformation("  Match: {TeamA} (#{TeamANum}) vs {TeamB} (#{TeamBNum})",
                    match.TeamAName, match.TeamANumber, match.TeamBName, match.TeamBNumber);

                if (!TeamExists(match.TeamANumber, match.TeamAName, teamByNumber))
                {
                    var error = new IngestionError(
                        errorCode: "TEAM_NOT_FOUND",
                        message: $"Team '{match.TeamAName}' (number {match.TeamANumber}) not found in database",
                        context: $"Match: {match.TeamAName} vs {match.TeamBName}",
                        isCritical: true
                    );
                    errors.Add(error);
                    _logger.LogError("Validation error - TEAM_NOT_FOUND: {Message}", error.Message);
                }
                else
                {
                    _logger.LogInformation("Validation - Team A found: '{TeamName}' (#{TeamNumber})", match.TeamAName, match.TeamANumber);
                }

                if (!TeamExists(match.TeamBNumber, match.TeamBName, teamByNumber))
                {
                    var error = new IngestionError(
                        errorCode: "TEAM_NOT_FOUND",
                        message: $"Team '{match.TeamBName}' (number {match.TeamBNumber}) not found in database",
                        context: $"Match: {match.TeamAName} vs {match.TeamBName}",
                        isCritical: true
                    );
                    errors.Add(error);
                    _logger.LogError("Validation error - TEAM_NOT_FOUND: {Message}", error.Message);
                }
                else
                {
                    _logger.LogInformation("Validation - Team B found: '{TeamName}' (#{TeamNumber})", match.TeamBName, match.TeamBNumber);
                }
            }

            // 7. Check for missing bowlers (non-critical)
            var missingBowlers = new HashSet<string>();
            foreach (var match in parsed.Matches)
            {
                foreach (var game in match.Games)
                {
                    foreach (var bowlerGame in game.BowlerGames)
                    {
                        var normalized = NormalizeString(bowlerGame.BowlerName);
                        if (!bowlersByNormalizedName.ContainsKey(normalized))
                        {
                            missingBowlers.Add(bowlerGame.BowlerName);
                        }
                    }
                }
            }

            if (missingBowlers.Any())
            {
                warnings.Add(new IngestionWarning(
                    code: "BOWLER_NOT_FOUND",
                    message: $"{missingBowlers.Count} bowler(s) not found in database. Their scores will be skipped.",
                    context: string.Join(", ", missingBowlers.Take(5)) +
                            (missingBowlers.Count > 5 ? $"... and {missingBowlers.Count - 5} more" : "")
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRITICAL: Unexpected exception during validation: {Message}", ex.Message);
            _logger.LogError("Exception type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);

            errors.Add(new IngestionError(
                errorCode: "VALIDATION_ERROR",
                message: $"Unexpected error during validation: {ex.Message}",
                context: ex.GetType().Name,
                isCritical: true,
                innerException: ex
            ));
        }

        return new ValidationResult(errors, warnings);
    }

    private static string NormalizeString(string s)
        => s.Trim().ToLowerInvariant();

    private static bool TeamExists(int teamNumber, string teamName, Dictionary<int, Data.Entities.Team> teamByNumber)
    {
        // Prefer a valid parsed team number
        if (teamNumber != 0 && teamByNumber.TryGetValue(teamNumber, out var byNumber))
            return true;

        // Fallback: try matching by normalized name
        var norm = NormalizeString(teamName);
        return teamByNumber.Values
            .Any(t => NormalizeString(t.Name) == norm);
    }
}

/// <summary>
/// Result of pre-flight validation.
/// </summary>
public class ValidationResult
{
    public List<IngestionError> Errors { get; }
    public List<IngestionWarning> Warnings { get; }

    public bool IsValid => !Errors.Any(e => e.IsCritical);
    public bool HasWarnings => Warnings.Any();

    public ValidationResult(List<IngestionError> errors, List<IngestionWarning> warnings)
    {
        Errors = errors ?? new();
        Warnings = warnings ?? new();
    }
}
