using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BowlingPredictor.Services;

namespace BowlingPredictor.Pages.Admin
{
    public class ImportBowlersModel : PageModel
    {
        private readonly BowlerListImporter _importer;

        public ImportBowlersModel(BowlerListImporter importer)
        {
            _importer = importer;
        }

        // Status message to show in the UI
        public string? ResultMessage { get; set; }

        // League selection (default Friday Night = 1)
        [BindProperty]
        public int LeagueId { get; set; } = 1;

        // This is where the file is bound from the form
        [BindProperty]
        public IFormFile? Upload { get; set; }

        public void OnGet()
        {
        }

        // PUT THE METHOD HERE
        public async Task<IActionResult> OnPostAsync()
        {
            if (Upload is null || Upload.Length == 0)
            {
                ResultMessage = "No file uploaded.";
                return Page();
            }

            using var stream = Upload.OpenReadStream();
            var result = await _importer.ImportAsync(stream, LeagueId);

            ResultMessage =
                $"Teams created: {result.TeamsCreated}, updated: {result.TeamsUpdated}. " +
                $"Bowlers created: {result.BowlersCreated}, updated: {result.BowlersUpdated}.";

            return Page();
        }

    }
}
