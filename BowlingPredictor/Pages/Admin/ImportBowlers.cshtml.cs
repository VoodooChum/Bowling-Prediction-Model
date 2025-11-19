using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BowlingPredictor.Services;
using BowlingPredictor.Data;
using BowlingPredictor.Data.Entities;
using Microsoft.Extensions.Logging;

namespace BowlingPredictor.Pages.Admin
{
    public class ImportBowlersModel : PageModel
    {
        private readonly BowlerListImporter _importer;
        private readonly LeagueDbContext _db;
        private readonly ILogger<ImportBowlersModel> _logger;

        public ImportBowlersModel(BowlerListImporter importer, LeagueDbContext db, ILogger<ImportBowlersModel> logger)
        {
            _importer = importer;
            _db = db;
            _logger = logger;
        }

        // Status message to show in the UI
        public string? ResultMessage { get; set; }

        // Error message if something goes wrong
        public string? ErrorMessage { get; set; }

        // Whether the import was successful
        public bool IsSuccess { get; set; }

        // League selection (defaults to Friday Night league)
        [BindProperty]
        public int LeagueId { get; set; }

        // Available leagues for dropdown
        public List<League> AvailableLeagues { get; set; } = new();

        // This is where the file is bound from the form
        [BindProperty]
        public IFormFile? Upload { get; set; }

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
            // Reload leagues for dropdown on form submission
            AvailableLeagues = _db.Leagues.OrderBy(l => l.Name).ToList();

            ErrorMessage = null;
            ResultMessage = null;
            IsSuccess = false;

            if (Upload is null || Upload.Length == 0)
            {
                ErrorMessage = "No file uploaded.";
                _logger.LogWarning("Bowler import attempted with no file");
                return Page();
            }

            try
            {
                using var stream = Upload.OpenReadStream();
                var result = await _importer.ImportAsync(stream, LeagueId);

                ResultMessage =
                    $"Teams created: {result.TeamsCreated}, updated: {result.TeamsUpdated}. " +
                    $"Bowlers created: {result.BowlersCreated}, updated: {result.BowlersUpdated}.";

                IsSuccess = true;

                _logger.LogInformation(
                    "Bowler import completed for league {LeagueId}: {TeamsCreated} teams created, {TeamsUpdated} updated, {BowlersCreated} bowlers created, {BowlersUpdated} updated",
                    LeagueId, result.TeamsCreated, result.TeamsUpdated, result.BowlersCreated, result.BowlersUpdated);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid file format for bowler import: {Message}", ex.Message);
                ErrorMessage = $"Invalid file format: {ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during bowler import");
                ErrorMessage = $"Error importing file: {ex.Message}";
            }

            return Page();
        }

    }
}
