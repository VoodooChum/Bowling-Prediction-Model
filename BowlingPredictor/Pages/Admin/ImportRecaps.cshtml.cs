using BowlingPredictor.Services.Recaps;
using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

public class ImportRecapsModel : PageModel
{
    private readonly RecapIngestService _ingest;
    private readonly RecapValidationService _validator;
    private readonly LeagueDbContext _db;
    private readonly ILogger<ImportRecapsModel> _logger;

    public ImportRecapsModel(
        RecapIngestService ingest,
        RecapValidationService validator,
        LeagueDbContext db,
        ILogger<ImportRecapsModel> logger)
    {
        _ingest = ingest;
        _validator = validator;
        _db = db;
        _logger = logger;
    }

    [BindProperty]
    public int LeagueId { get; set; }

    [BindProperty]
    public List<IFormFile> Files { get; set; } = new();

    public List<FileImportResult> Results { get; set; } = new();

    // Available leagues for dropdown
    public List<League> AvailableLeagues { get; set; } = new();

    public void OnGet()
    {
        // Load all leagues for the dropdown
        AvailableLeagues = _db.Leagues.OrderBy(l => l.Name).ToList();

        // Find the Friday Night league and use its ID as default
        var fridayNightLeague = AvailableLeagues.FirstOrDefault(l => l.Name == "Friday Night");
        if (fridayNightLeague != null)
        {
            LeagueId = fridayNightLeague.LeagueId;
        }
        else if (AvailableLeagues.Any())
        {
            // If no Friday Night league, default to first league
            LeagueId = AvailableLeagues.First().LeagueId;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Results = new List<FileImportResult>();

        _logger.LogInformation("ImportRecaps OnPostAsync called with LeagueId={LeagueId}", LeagueId);

        if (Files == null || Files.Count == 0)
        {
            Results.Add(new FileImportResult(
                fileName: "No file selected",
                skipped: true,
                message: "No files uploaded.",
                matchesCreated: 0,
                gamesCreated: 0,
                scoresCreated: 0,
                errors: new(),
                warnings: new(),
                hasErrors: true
            ));
            return Page();
        }

        foreach (var file in Files)
        {
            if (file.Length == 0)
            {
                Results.Add(new FileImportResult(
                    fileName: file.FileName,
                    skipped: true,
                    message: "Empty file.",
                    matchesCreated: 0,
                    gamesCreated: 0,
                    scoresCreated: 0,
                    errors: new(),
                    warnings: new(),
                    hasErrors: true
                ));
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();

                // Run validation first
                var validationResult = await _validator.ValidateBeforeIngestAsync(stream, LeagueId);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Validation failed for file {FileName}: {ErrorCount} errors",
                        file.FileName, validationResult.Errors.Count);

                    Results.Add(new FileImportResult(
                        fileName: file.FileName,
                        skipped: true,
                        message: "Pre-flight validation failed",
                        matchesCreated: 0,
                        gamesCreated: 0,
                        scoresCreated: 0,
                        errors: validationResult.Errors,
                        warnings: validationResult.Warnings,
                        hasErrors: true
                    ));
                    continue;
                }

                // Reset stream position for ingestion
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);

                // Perform actual ingestion
                var ingestResult = await _ingest.IngestAsync(stream, LeagueId, file.FileName);

                _logger.LogInformation(
                    "Recap ingestion completed for {FileName}: {MatchCount} matches, {GameCount} games, {ScoreCount} scores",
                    file.FileName, ingestResult.MatchesCreated, ingestResult.GamesCreated, ingestResult.ScoresCreated);

                Results.Add(new FileImportResult(
                    fileName: file.FileName,
                    skipped: ingestResult.Skipped,
                    message: ingestResult.Skipped
                        ? ingestResult.Reason ?? "Skipped"
                        : $"Success: {ingestResult.MatchesCreated} matches, {ingestResult.GamesCreated} games, {ingestResult.ScoresCreated} scores",
                    matchesCreated: ingestResult.MatchesCreated,
                    gamesCreated: ingestResult.GamesCreated,
                    scoresCreated: ingestResult.ScoresCreated,
                    errors: ingestResult.Errors ?? new(),
                    warnings: ingestResult.Warnings ?? new(),
                    hasErrors: ingestResult.HasErrors
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error importing recap file {FileName}", file.FileName);

                Results.Add(new FileImportResult(
                    fileName: file.FileName,
                    skipped: true,
                    message: $"Error: {ex.Message}",
                    matchesCreated: 0,
                    gamesCreated: 0,
                    scoresCreated: 0,
                    errors: new()
                    {
                        new IngestionError(
                            "FILE_IMPORT_ERROR",
                            ex.Message,
                            isCritical: true,
                            innerException: ex
                        )
                    },
                    warnings: new(),
                    hasErrors: true
                ));
            }
        }

        return Page();
    }

    public class FileImportResult
    {
        public string FileName { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; }
        public int MatchesCreated { get; set; }
        public int GamesCreated { get; set; }
        public int ScoresCreated { get; set; }
        public List<IngestionError> Errors { get; set; }
        public List<IngestionWarning> Warnings { get; set; }
        public bool HasErrors { get; set; }

        public FileImportResult(
            string fileName,
            bool skipped,
            string message,
            int matchesCreated,
            int gamesCreated,
            int scoresCreated,
            List<IngestionError>? errors = null,
            List<IngestionWarning>? warnings = null,
            bool hasErrors = false)
        {
            FileName = fileName;
            Skipped = skipped;
            Message = message;
            MatchesCreated = matchesCreated;
            GamesCreated = gamesCreated;
            ScoresCreated = scoresCreated;
            Errors = errors ?? new();
            Warnings = warnings ?? new();
            HasErrors = hasErrors;
        }
    }
}

