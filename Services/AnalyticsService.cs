using HajjVR.Data;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Services;

public record DashboardSummary(int TotalJamaah, int TotalPembimbing, double AvgProgressPercent,
    int UmrahCompleted, int HajiCompleted, int Activities24h, int ChatSessions);

public record RitualStat(RitualType Ritual, string Name, int NotStarted, int InProgress, int Completed);

public record HeatCell(string Zone, int Hour, int Count);

public record ZoneNow(string Zone, string Label, int Count, DateTime Timestamp);

public record LeaderboardEntry(int UserId, string DisplayName, string GroupName, int Points, int BadgeCount, int RitualsCompleted);

public class AnalyticsService(IDbContextFactory<AppDbContext> dbFactory)
{
    public static string RitualName(RitualType r) => r switch
    {
        RitualType.Ihram => "Ihram & Niat",
        RitualType.Thawaf => "Thawaf",
        RitualType.Sai => "Sa'i",
        RitualType.Tahalul => "Tahalul",
        RitualType.WukufArafah => "Wukuf di Arafah",
        RitualType.MabitMuzdalifah => "Mabit Muzdalifah",
        RitualType.LemparJumrah => "Lempar Jumrah",
        RitualType.MabitMina => "Mabit Mina",
        RitualType.ThawafIfadah => "Thawaf Ifadah",
        RitualType.ThawafWada => "Thawaf Wada'",
        RitualType.ZiarahNabawi => "Ziarah Nabawi",
        _ => r.ToString()
    };

    public static string ZoneLabel(string zone) => zone switch
    {
        "mataf" => "Area Thawaf (Mataf)",
        "masaa" => "Area Sa'i (Mas'a)",
        "mosque" => "Masjidil Haram",
        "arafah" => "Padang Arafah",
        "muzdalifah" => "Muzdalifah",
        "mina" => "Mina / Jamarat",
        "nabawi" => "Masjid Nabawi",
        "raudhah" => "Raudhah",
        _ => zone
    };

    public async Task<DashboardSummary> GetSummaryAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var totalJamaah = await db.Users.CountAsync(u => u.Role == Roles.Jamaah);
        var totalPembimbing = await db.Users.CountAsync(u => u.Role == Roles.Pembimbing);

        var progress = await db.RitualProgresses.AsNoTracking().ToListAsync();
        double avg = progress.Count == 0 ? 0 :
            100.0 * progress.Count(p => p.Status == ProgressStatus.Completed) / progress.Count;

        var umrahRituals = new[] { RitualType.Ihram, RitualType.Thawaf, RitualType.Sai, RitualType.Tahalul };
        var hajiRituals = new[] { RitualType.WukufArafah, RitualType.MabitMuzdalifah, RitualType.LemparJumrah, RitualType.MabitMina, RitualType.ThawafIfadah };
        var byUser = progress.GroupBy(p => p.UserId).ToList();
        int umrahDone = byUser.Count(g => umrahRituals.All(r => g.Any(p => p.Ritual == r && p.Status == ProgressStatus.Completed)));
        int hajiDone = byUser.Count(g => hajiRituals.All(r => g.Any(p => p.Ritual == r && p.Status == ProgressStatus.Completed)));

        var since = DateTime.UtcNow.AddHours(-24);
        int act = await db.ActivityLogs.CountAsync(a => a.Timestamp >= since);
        int chats = await db.ChatSessions.CountAsync();
        return new DashboardSummary(totalJamaah, totalPembimbing, Math.Round(avg, 1), umrahDone, hajiDone, act, chats);
    }

    public async Task<List<RitualStat>> GetRitualStatsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.RitualProgresses.AsNoTracking()
            .GroupBy(p => new { p.Ritual, p.Status })
            .Select(g => new { g.Key.Ritual, g.Key.Status, Count = g.Count() })
            .ToListAsync();
        return Enum.GetValues<RitualType>().Select(r => new RitualStat(
            r, RitualName(r),
            rows.Where(x => x.Ritual == r && x.Status == ProgressStatus.NotStarted).Sum(x => x.Count),
            rows.Where(x => x.Ritual == r && x.Status == ProgressStatus.InProgress).Sum(x => x.Count),
            rows.Where(x => x.Ritual == r && x.Status == ProgressStatus.Completed).Sum(x => x.Count)
        )).ToList();
    }

    /// <summary>Heatmap keramaian: rata-rata count per zona per jam (24 jam terakhir).</summary>
    public async Task<List<HeatCell>> GetHeatmapAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = DateTime.UtcNow.AddHours(-24);
        var snaps = await db.CrowdSnapshots.AsNoTracking().Where(c => c.Timestamp >= since).ToListAsync();
        return snaps.GroupBy(s => new { s.Zone, s.Timestamp.Hour })
            .Select(g => new HeatCell(g.Key.Zone, g.Key.Hour, (int)g.Average(x => x.Count)))
            .ToList();
    }

    public async Task<List<ZoneNow>> GetZonesNowAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var snaps = await db.CrowdSnapshots.AsNoTracking()
            .GroupBy(s => s.Zone)
            .Select(g => g.OrderByDescending(x => x.Timestamp).First())
            .ToListAsync();
        return snaps.Select(s => new ZoneNow(s.Zone, ZoneLabel(s.Zone), s.Count, s.Timestamp))
            .OrderByDescending(z => z.Count).ToList();
    }

    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(int top = 20)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Role == Roles.Jamaah).ToListAsync();
        var badges = await db.UserBadges.AsNoTracking().Include(b => b.Badge).ToListAsync();
        var completed = await db.RitualProgresses.AsNoTracking()
            .Where(p => p.Status == ProgressStatus.Completed)
            .GroupBy(p => p.UserId).Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return users.Select(u =>
        {
            var ub = badges.Where(b => b.UserId == u.Id).ToList();
            int rituals = completed.GetValueOrDefault(u.Id);
            int points = ub.Sum(b => b.Badge?.Points ?? 0) + rituals * 10;
            return new LeaderboardEntry(u.Id, u.DisplayName, u.Profile?.GroupName ?? "-", points, ub.Count, rituals);
        }).OrderByDescending(e => e.Points).Take(top).ToList();
    }

    /// <summary>Progres per jamaah (untuk laporan).</summary>
    public async Task<List<(AppUser User, JamaahProfile? Profile, int Completed, int Total)>> GetJamaahProgressAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Role == Roles.Jamaah).OrderBy(u => u.DisplayName).ToListAsync();
        var progress = await db.RitualProgresses.AsNoTracking().ToListAsync();
        int total = Enum.GetValues<RitualType>().Length;
        return users.Select(u => (u, u.Profile,
            progress.Count(p => p.UserId == u.Id && p.Status == ProgressStatus.Completed), total)).ToList();
    }

    public async Task LogActivityAsync(int? userId, string action, string detail = "")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.ActivityLogs.Add(new ActivityLog { UserId = userId, Action = action, Detail = detail });
        await db.SaveChangesAsync();
    }
}
