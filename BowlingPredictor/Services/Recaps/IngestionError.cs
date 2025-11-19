namespace BowlingPredictor.Services.Recaps;

/// <summary>
/// Represents an error that occurred during recap ingestion.
/// </summary>
public class IngestionError
{
    /// <summary>
    /// Error code for programmatic handling (e.g., "BOWLER_NOT_FOUND", "TEAM_MISMATCH")
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Context about where the error occurred (e.g., "Game 2, Team A, Bowler 'John Doe'")
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Whether this error is critical (stops processing) or recoverable (continues)
    /// </summary>
    public bool IsCritical { get; set; }

    /// <summary>
    /// Inner exception details (for logging)
    /// </summary>
    public Exception? InnerException { get; set; }

    public IngestionError(
        string errorCode,
        string message,
        string? context = null,
        bool isCritical = true,
        Exception? innerException = null)
    {
        ErrorCode = errorCode;
        Message = message;
        Context = context;
        IsCritical = isCritical;
        InnerException = innerException;
    }
}

/// <summary>
/// Represents a warning that occurred during recap ingestion (non-fatal).
/// </summary>
public class IngestionWarning
{
    /// <summary>
    /// Warning code (e.g., "HANDICAP_MISSING", "DUPLICATE_MATCH")
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly warning message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Context about where the warning occurred
    /// </summary>
    public string? Context { get; set; }

    public IngestionWarning(string code, string message, string? context = null)
    {
        Code = code;
        Message = message;
        Context = context;
    }
}
