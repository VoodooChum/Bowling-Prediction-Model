using BowlingPredictor.Services.Recaps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class ImportRecapsModel : PageModel
{
    private readonly RecapIngestService _ingest;

    public ImportRecapsModel(RecapIngestService ingest)
    {
        _ingest = ingest;
    }

    [BindProperty]
    public int LeagueId { get; set; } = 1;   // Friday Night default

    [BindProperty]
    public List<IFormFile> Files { get; set; } = new();

    public List<FileImportResult> Results { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Results = new List<FileImportResult>();

        if (Files == null || Files.Count == 0)
        {
            Results.Add(new FileImportResult("No file selected", true, "No files uploaded.", 0, 0, 0));
            return Page();
        }

        foreach (var file in Files)
        {
            if (file.Length == 0)
            {
                Results.Add(new FileImportResult(file.FileName, true, "Empty file.", 0, 0, 0));
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var res = await _ingest.IngestAsync(stream, LeagueId, file.FileName);

                Results.Add(new FileImportResult(
                    file.FileName,
                    res.Skipped,
                    res.Skipped ? res.Reason ?? "Skipped" : "Imported",
                    res.MatchesCreated,
                    res.GamesCreated,
                    res.ScoresCreated
                ));
            }
            catch (Exception ex)
            {
                Results.Add(new FileImportResult(
                    file.FileName,
                    true,
                    $"Error: {ex.Message}",
                    0, 0, 0));
            }
        }

        return Page();
    }

    public record FileImportResult(
        string FileName,
        bool Skipped,
        string Message,
        int MatchesCreated,
        int GamesCreated,
        int ScoresCreated
    );
}

