using BowlingPredictor.Data;
using BowlingPredictor.Services;
using Microsoft.EntityFrameworkCore;
using BowlingPredictor.Services.Recaps;
using Serilog;

namespace BowlingPredictor
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // --- Configure Serilog ---
            builder.Host.UseSerilog((context, configuration) =>
            {
                configuration
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File(
                        path: "logs/bowling-predictor-.txt",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            });

            // --- Configure services ---
            builder.Services.AddRazorPages();
            builder.Services.AddDbContext<LeagueDbContext>(opt =>
                opt.UseSqlServer(builder.Configuration.GetConnectionString("LeagueDb")));

            builder.Services.AddScoped<BowlerListImporter>();
            builder.Services.AddScoped<IRecapParser, BlsRecapParser>();
            builder.Services.AddScoped<RecapIngestService>();
            builder.Services.AddScoped<RecapValidationService>();


            var app = builder.Build();

            // --- Seed database (runs once) ---
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LeagueDbContext>();

                // Apply any pending migrations
                await db.Database.MigrateAsync();

                await DbSeeder.SeedAsync(db);

                // List all leagues
                var leagues = await db.Leagues
                    .AsNoTracking()
                    .ToListAsync();

                Console.WriteLine($"[DBG] Seed check: {leagues.Count} leagues in database");
                foreach (var l in leagues)
                {
                    Console.WriteLine($"[DBG]  -> {l.LeagueId}: {l.Name}");
                }

                // Optionally, show teams for the first league (if any)
                if (leagues.Count > 0)
                {
                    var firstLeagueId = leagues[0].LeagueId;
                    var leagueWithTeams = await db.Leagues
                        .Include(l => l.Teams)
                        .AsNoTracking()
                        .FirstAsync(l => l.LeagueId == firstLeagueId);

                    Console.WriteLine($"[DBG] Teams in \"{leagueWithTeams.Name}\": {leagueWithTeams.Teams.Count}");
                    foreach (var t in leagueWithTeams.Teams.OrderBy(t => t.Number))
                    {
                        Console.WriteLine($"[DBG]   - #{t.Number}: {t.Name}");
                    }
                }
            }

            // --- Normal middleware pipeline ---
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.MapRazorPages();

            await app.RunAsync();
        }
    }
}
