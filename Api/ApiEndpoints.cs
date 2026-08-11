using HajjVR.Data;
using HajjVR.Services;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Api;

/// <summary>
/// REST API (Minimal API) untuk integrasi eksternal. Terlihat di Swagger (/swagger).
/// Autentikasi: header X-Api-Key (nilai dari setting Api:Key) ATAU cookie login aplikasi.
/// </summary>
public static class ApiEndpoints
{
    public static void MapHajjApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("HajjVR");
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var settings = http.RequestServices.GetRequiredService<SettingsService>();
            var expected = settings.Get("Api:Key", "hajjvr-demo-key");
            var provided = http.Request.Headers["X-Api-Key"].ToString();
            if (provided == expected || http.User.Identity?.IsAuthenticated == true)
                return await next(ctx);
            return Results.Unauthorized();
        });

        // ---------- Jamaah ----------
        api.MapGet("/jamaah", async (IDbContextFactory<AppDbContext> f, int page = 1, int pageSize = 20, string? q = null) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var query = db.Users.AsNoTracking().Include(u => u.Profile).Where(u => u.Role == Roles.Jamaah);
            if (!string.IsNullOrEmpty(q)) query = query.Where(u => u.DisplayName.ToLower().Contains(q.ToLower()));
            var total = await query.CountAsync();
            var items = await query.OrderBy(u => u.DisplayName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(u => new
                {
                    u.Id, u.UserName, u.DisplayName, u.Email,
                    Group = u.Profile!.GroupName, u.Profile.PackageType, u.Profile.Nationality
                }).ToListAsync();
            return Results.Ok(new { total, page, pageSize, items });
        }).WithSummary("Daftar jamaah (paging + pencarian)");

        api.MapGet("/jamaah/{id:int}", async (int id, IDbContextFactory<AppDbContext> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var u = await db.Users.AsNoTracking().Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == id && x.Role == Roles.Jamaah);
            if (u is null) return Results.NotFound();
            var progress = await db.RitualProgresses.AsNoTracking().Where(p => p.UserId == id)
                .Select(p => new { Ritual = p.Ritual.ToString(), Nama = AnalyticsService.RitualName(p.Ritual), Status = p.Status.ToString(), p.CompletedAt })
                .ToListAsync();
            return Results.Ok(new { u.Id, u.DisplayName, u.Email, Profil = u.Profile, Progres = progress });
        }).WithSummary("Detail jamaah + progres ritual");

        // ---------- Progres ----------
        api.MapGet("/progress/summary", async (AnalyticsService analytics) =>
            Results.Ok(await analytics.GetSummaryAsync()))
            .WithSummary("Ringkasan statistik aplikasi");

        api.MapGet("/progress/rituals", async (AnalyticsService analytics) =>
            Results.Ok(await analytics.GetRitualStatsAsync()))
            .WithSummary("Statistik progres per ritual");

        api.MapPost("/progress/{userId:int}/{ritual}", async (int userId, RitualType ritual, ProgressStatus status,
            IDbContextFactory<AppDbContext> f, GamificationService gamification) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var p = await db.RitualProgresses.FirstOrDefaultAsync(x => x.UserId == userId && x.Ritual == ritual);
            if (p is null)
            {
                p = new RitualProgress { UserId = userId, Ritual = ritual };
                db.RitualProgresses.Add(p);
            }
            p.Status = status;
            if (status == ProgressStatus.InProgress) p.StartedAt ??= DateTime.UtcNow;
            if (status == ProgressStatus.Completed) p.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            var newBadges = await gamification.EvaluateAsync(userId);
            return Results.Ok(new { updated = true, newBadges = newBadges.Select(b => b.Name) });
        }).WithSummary("Perbarui status ritual jamaah");

        // ---------- Lokasi ----------
        api.MapGet("/locations", async (IDbContextFactory<AppDbContext> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(await db.Locations.AsNoTracking().ToListAsync());
        }).WithSummary("Semua lokasi suci (koordinat + deskripsi)");

        // ---------- Analytics ----------
        api.MapGet("/analytics/crowd", async (AnalyticsService analytics) =>
            Results.Ok(await analytics.GetZonesNowAsync()))
            .WithSummary("Keramaian terkini per zona");

        api.MapGet("/analytics/heatmap", async (AnalyticsService analytics) =>
            Results.Ok(await analytics.GetHeatmapAsync()))
            .WithSummary("Data heatmap keramaian 24 jam");

        api.MapGet("/analytics/leaderboard", async (AnalyticsService analytics) =>
            Results.Ok(await analytics.GetLeaderboardAsync(20)))
            .WithSummary("Papan peringkat gamifikasi");

        // ---------- Dokumen ----------
        api.MapGet("/documents", async (IDbContextFactory<AppDbContext> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(await db.Documents.AsNoTracking()
                .Select(d => new { d.Id, d.Title, d.Kind, d.FileName, d.Url, d.UploadedAt }).ToListAsync());
        }).WithSummary("Daftar dokumen/panduan");

        // ---------- Pencarian semantik ----------
        api.MapGet("/search", async (string q, SemanticSearchService search) =>
            Results.Ok(await search.SearchAsync(q, 5)))
            .WithSummary("Pencarian semantik lokasi/ritual/dokumen");

        // ---------- Ekspor laporan ----------
        api.MapGet("/reports/progress.xlsx", async (ExportService export) =>
        {
            var bytes = await export.ExportProgressExcelAsync();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "laporan-progres-jamaah.xlsx");
        }).WithSummary("Unduh laporan progres (Excel)");

        api.MapGet("/reports/progress.pdf", async (ExportService export) =>
        {
            var bytes = await export.ExportProgressPdfAsync();
            return Results.File(bytes, "application/pdf", "laporan-progres-jamaah.pdf");
        }).WithSummary("Unduh laporan progres (PDF)");

        api.MapGet("/reports/jamaah/{id:int}.pdf", async (int id, ExportService export) =>
        {
            var bytes = await export.ExportJamaahPdfAsync(id);
            return Results.File(bytes, "application/pdf", $"laporan-jamaah-{id}.pdf");
        }).WithSummary("Unduh laporan perjalanan satu jamaah (PDF)");
    }
}
