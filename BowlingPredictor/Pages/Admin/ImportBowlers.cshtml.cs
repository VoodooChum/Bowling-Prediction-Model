using BowlingPredictor.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class ImportBowlersModel : PageModel
{
    private readonly BowlerListImporter _importer;
    public string? ResultMessage { get; set; }

    public ImportBowlersModel(BowlerListImporter importer) => _importer = importer;

    [BindProperty] public int LeagueId { get; set; } = 1;
    [BindProperty] public IFormFile? File { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (File is null || File.Length == 0)
        {
            ResultMessage = "No file uploaded.";
            return Page();
        }

        using var stream = File.OpenReadStream();
        var result = await _importer.ImportAsync(stream, LeagueId);
        ResultMessage = $"Teams: +{result.TeamsCreated}/~{result.TeamsUpdated}, " +
                        $"Bowlers: +{result.BowlersCreated}/~{result.BowlersUpdated}";
        return Page();
    }
}
