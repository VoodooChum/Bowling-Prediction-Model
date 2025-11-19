# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**BowlingPredictor** is an ASP.NET Core 10.0 web application designed to track bowling league matches, individual bowler performance, and eventually predict match outcomes using machine learning models.

- **Technology Stack:** C# .NET 10.0, ASP.NET Core, Razor Pages, Entity Framework Core 10.0, SQL Server (LocalDB)
- **Frontend:** Bootstrap 5, jQuery
- **Database:** SQL Server LocalDB with automatic migrations

## Quick Start

### Prerequisites
- .NET 10 SDK
- SQL Server LocalDB (or SQL Server instance)
- Visual Studio or VS Code

### Setup & Running
```bash
# Restore NuGet packages
dotnet restore

# Apply database migrations (auto-runs on startup)
dotnet ef database update

# Run the development server
dotnet run

# Application launches on http://localhost:5279 or https://localhost:7093
```

The database will be automatically seeded with initial data (Friday Night league, 2 default teams) on first run via `DbSeeder.SeedAsync()`.

### Development Configuration
- **Connection String:** `(localdb)\MSSQLLocalDB` / database `BowlingPredictor`
- **Configuration File:** `appsettings.Development.json`
- Environment variable: `ASPNETCORE_ENVIRONMENT=Development`

## Project Architecture

### High-Level Flow

```
Presentation (Razor Pages)
    ↓
Page Models (Dependency Injection)
    ↓
Service Layer (Business Logic)
    ├─ BowlerListImporter        (Excel → Bowlers/Teams)
    ├─ BlsRecapParser            (PDF text → Parsed data)
    └─ RecapIngestService        (Orchestration → DB)
    ↓
Data Layer (EF Core DbContext)
    ↓
Database (SQL Server)
```

### Core Components

#### 1. **Data Layer** (`Data/` folder)

**LeagueDbContext** (`LeagueDbContext.cs:Program.cs:16-17`)
- Single DbContext managing all entities
- 8 DbSets: Leagues, Teams, Bowlers, Matches, Games, GameScores, BowlerSeasonAggs, ModelVersions
- Enforced relationships:
  - League → (1:N) Teams, Bowlers, Matches, ModelVersions
  - Team → (1:N) Bowlers, Games (as TeamA/TeamB in Match)
  - Match → (1:N) Games (max 3 per match)
  - Game → (1:N) GameScores (one per bowler per game)
  - Bowler → (1:N) GameScores, BowlerSeasonAgg

**Composite Keys & Unique Constraints:**
- BowlerSeasonAgg: composite key (LeagueId + BowlerId)
- Match: unique index (LeagueId + WeekNo + TeamAId + TeamBId) prevents duplicate matches
- Game: unique index (MatchId + GameNo) enforces 1-3 games per match
- GameScore: unique index (GameId + BowlerId) prevents duplicate scores

**DbSeeder** (`Data/DbSeeder.cs`)
- Idempotent seeding: creates initial league and teams only if they don't exist
- Default data: "Friday Night" league with Team 1 & Team 2

#### 2. **Service Layer** (`Services/` folder)

**BowlerListImporter** (`Services/BowlerListImporter.cs`)
- Imports bowler rosters from Excel files
- Uses ClosedXML for parsing
- Creates or updates teams and bowlers in database
- Returns `ImportResult` with counts of created/updated entities

**BlsRecapParser** (`Services/Recaps/BlsRecapParser.cs`)
- Parses BLS-format bowling recap PDFs using UglyToad.PdfPig
- Extracts: match date, week number, team names, lane pairs, individual bowler scores (frames 1-10 + spare/strike pins)
- Uses compiled Regex patterns for robust text extraction
- Returns `ParsedRecap` DTO containing multiple `ParsedMatch` objects
- **Currently Under Development:** ParsedRecapModels and parser logic recently updated to handle all weeks properly

**RecapIngestService** (`Services/Recaps/RecapIngestService.cs`)
- Orchestrates PDF parsing → EF Core entity creation → database ingestion
- Deduplicates by (LeagueId + Date) to prevent duplicate matches
- Creates Match → Game (1-3) → GameScore entities
- Returns `RecapIngestResult` with counts of created records
- **Status:** All weeks properly ingested for matches as of recent commits

**IRecapParser Interface** (`Services/Recaps/IRecapParser.cs`)
- Allows swapping parser implementations (BLS format, stub parser for testing)

#### 3. **Presentation Layer** (`Pages/` folder)

**Admin Pages:**
- `Admin/ImportBowlers.cshtml.cs` — File upload & processing for bowler rosters
- `Admin/ImportRecaps.cshtml.cs` — File upload & processing for recap PDFs

**Public Pages:**
- `Teams.cshtml.cs` — Display teams and bowlers roster
- `Weeks.cshtml.cs` — Display weekly match results (currently a stub)
- `Index.cshtml.cs` & `Privacy.cshtml.cs` — Basic pages

All pages inherit from Razor PageModel with dependency-injected services from Program.cs.

### Database Schema

**Key Entities:**
- **League** — Container for a season's data
- **Team** — Team in a league (identified by number + name)
- **Bowler** — Individual bowler (belongs to a team, can be marked as substitute)
- **Match** — Game between two teams in a specific week
- **Game** — Individual game (1 of 3) within a match
- **GameScore** — A bowler's score in one game (frames 1-10 + pins)
- **BowlerSeasonAgg** — Season statistics for a bowler (mean, std dev, reliability) — *not yet populated*
- **ModelVersion** — Serialized prediction model params — *not yet used*

## Common Development Tasks

### Adding a New Razor Page
1. Create `Pages/MyPage.cshtml` and `Pages/MyPage.cshtml.cs`
2. Inject services in the PageModel constructor (e.g., `LeagueDbContext`, `BowlerListImporter`)
3. Use `builder.Services.AddRazorPages()` — already configured in Program.cs:15

### Creating a New Entity & Migration
1. Add class to `Data/Entities/`
2. Add DbSet to LeagueDbContext.cs
3. Configure relationships in LeagueDbContext.OnModelCreating()
4. Generate migration: `dotnet ef migrations add YourMigrationName`
5. Apply: `dotnet ef database update`

### Running Database Migrations
```bash
# Create a new migration after entity changes
dotnet ef migrations add [MigrationName]

# List pending migrations
dotnet ef migrations list

# Update database to latest
dotnet ef database update

# Revert to previous migration
dotnet ef database update [PreviousMigrationName]
```

### Dependency Injection Pattern
Services registered in `Program.cs:19-21`:
```csharp
builder.Services.AddScoped<BowlerListImporter>();
builder.Services.AddScoped<IRecapParser, BlsRecapParser>();
builder.Services.AddScoped<RecapIngestService>();
```

Inject in page models:
```csharp
public class ImportBowlersModel : PageModel
{
    private readonly BowlerListImporter _importer;

    public ImportBowlersModel(BowlerListImporter importer)
    {
        _importer = importer;
    }
}
```

## Current Development State

### Completed
- ✅ Database schema with comprehensive relationships
- ✅ Bowler list import from Excel (BowlerListImporter)
- ✅ Match/Game entity creation from recap PDFs
- ✅ Teams and bowlers basic roster management
- ✅ Home page with navigation links to import pages
- ✅ League dropdown selectors (Admin/ImportBowlers and Admin/ImportRecaps pages)
- ✅ RecapValidationService for pre-flight validation
- ✅ PDF text extraction and lane header parsing

### In Progress / TODO (November 19, 2025)
- 🔄 **PDF Bowler Parsing** — BlsRecapParser bowler data extraction from concatenated text
  - **Status:** Diagnosed root cause — PDF text is concatenated with NO LINE BREAKS
  - **Issue:** Each lane segment contains TWO teams' data side-by-side (left/right columns)
  - **Current Fix:** Modified ParseTeamSegment() to extract only first team's bowlers (lines 286-304)
  - **Next Steps:**
    1. Test the fix to verify all 5 bowlers from each lane parse correctly
    2. Verify GameScores are created with correct bowler/score mappings
    3. Remove debug logging from BlsRecapParser (Console.WriteLine calls)
  - **Root Cause Analysis:** PDF extraction produces concatenated string like:
    ```
    "Lane 11 - Ain't that Nice...NameAvgHDCP[Team 1 bowlers]TotalTotalNameAvgHDCP[Team 2 bowlers]TotalTotal===="
    ```
    The parser needs to stop at the FIRST "TotalTotal" to avoid capturing Team 2's data.

- ⚠️ GameScore population — Currently 0 scores created (bowler parsing is the blocker)
- ⚠️ BowlerSeasonAgg calculation — aggregate stats per bowler per season
- ⚠️ ModelVersion population — training and storing prediction models
- ⚠️ Match outcome predictions — UI and logic
- ⚠️ Weeks.cshtml dashboard — display weekly results and standings
- ⚠️ Unit tests — no test project currently exists

## Key Files & Responsibilities

| File | Purpose |
|------|---------|
| `Program.cs` | DI configuration, middleware setup, database seeding |
| `LeagueDbContext.cs` | EF Core DbContext with all entity mappings |
| `Services/BowlerListImporter.cs` | Excel import orchestration |
| `Services/Recaps/BlsRecapParser.cs` | PDF text parsing (regex-based) |
| `Services/Recaps/RecapIngestService.cs` | PDF → Match/Game/GameScore creation |
| `Data/DbSeeder.cs` | Initial database population |
| `appsettings.Development.json` | DB connection string & logging config |
| `Pages/Admin/ImportBowlers.cshtml.cs` | File upload UI for rosters |
| `Pages/Admin/ImportRecaps.cshtml.cs` | File upload UI for PDFs |

## Important Design Notes

1. **PDF Text Extraction Challenges:** The BLS recap PDFs extracted via UglyToad.PdfPig produce concatenated text with NO LINE BREAKS. Each segment contains data for TWO teams in a side-by-side (columnar) layout:
   - Format: `NameAvgHDCP[Team 1 bowlers: Name1bk###...Score1 Score2 Score3]TotalTotal[Team 2 bowlers:...]TotalTotal====`
   - **Solution:** ParseTeamSegment() extracts only the FIRST team's data (from first "NameAvgHDCP" to first "TotalTotal")
   - **Regex Pattern:** `([A-Z][A-Za-z' .-]*?)bk(\d{3})(\d{2})(\d{3})(\d{3})(\d{3})` matches: Name + "bk" + 3-digit ID + 2-digit code + three 3-digit scores
   - **Validation:** Sanity check rejects scores > 300 (impossible in bowling)

2. **Deduplication:** RecapIngestService prevents duplicate Match records by querying (LeagueId + Date) before creation.

3. **Cascading Deletes:** Games cascade delete when a Match is deleted; GameScores cascade delete when a Game is deleted. Teams cannot be deleted if they have bowlers (Restrict behavior).

4. **Composite Key:** BowlerSeasonAgg uses (LeagueId + BowlerId) as composite key — supports multiple seasons per bowler.

5. **No explicit API layer:** All data access goes through EF Core DbContext in page models or services. Consider adding API endpoints or a repository layer if service complexity grows.

## Testing

Currently no test projects exist. To add tests:
```bash
dotnet new xunit -n BowlingPredictor.Tests
dotnet add BowlingPredictor.Tests reference BowlingPredictor
```

Consider testing:
- PDF parser regex patterns
- Excel importer data validation
- Database seeding idempotence
- Entity relationship constraints

## Build & Deployment

```bash
# Development build
dotnet build

# Release build
dotnet build --configuration Release

# Publish for deployment
dotnet publish --configuration Release -o ./publish
```

Development mode enables detailed error pages and verbose logging. Production mode requires environment variable `ASPNETCORE_ENVIRONMENT=Production` and uses standard error handling.

## External Dependencies

- **ClosedXML 0.105.0** — Excel file reading
- **UglyToad.PdfPig 0.1.11** — PDF text extraction
- **DocumentFormat.OpenXml 3.3.0** — Office format support
- **EntityFramework Core 10.0.0** — ORM & migrations
- **Bootstrap 5** — Frontend styling
- **jQuery** — DOM manipulation

