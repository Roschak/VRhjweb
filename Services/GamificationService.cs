using HajjVR.Data;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Services;

/// <summary>Pemberian badge otomatis berdasarkan progres ritual.</summary>
public class GamificationService(IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly (string Code, Func<HashSet<RitualType>, bool> Rule)[] Rules =
    [
        ("first-step", done => done.Count > 0),
        ("thawaf-master", done => done.Contains(RitualType.Thawaf)),
        ("sai-runner", done => done.Contains(RitualType.Sai)),
        ("wukuf-arafah", done => done.Contains(RitualType.WukufArafah)),
        ("jumrah-warrior", done => done.Contains(RitualType.LemparJumrah)),
        ("umrah-complete", done => done.Contains(RitualType.Ihram) && done.Contains(RitualType.Thawaf)
                                && done.Contains(RitualType.Sai) && done.Contains(RitualType.Tahalul)),
        ("hajj-complete", done => done.Contains(RitualType.WukufArafah) && done.Contains(RitualType.MabitMuzdalifah)
                                && done.Contains(RitualType.LemparJumrah) && done.Contains(RitualType.MabitMina)
                                && done.Contains(RitualType.ThawafIfadah)),
    ];

    /// <summary>Evaluasi & berikan badge baru. Kembalikan daftar badge yang baru didapat.</summary>
    public async Task<List<Badge>> EvaluateAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var done = (await db.RitualProgresses
                .Where(p => p.UserId == userId && p.Status == ProgressStatus.Completed)
                .Select(p => p.Ritual).ToListAsync()).ToHashSet();
        var owned = (await db.UserBadges.Where(b => b.UserId == userId).Select(b => b.BadgeId).ToListAsync()).ToHashSet();
        var allBadges = await db.Badges.ToListAsync();

        var awarded = new List<Badge>();
        foreach (var (code, rule) in Rules)
        {
            var badge = allBadges.FirstOrDefault(b => b.Code == code);
            if (badge is null || owned.Contains(badge.Id) || !rule(done)) continue;
            db.UserBadges.Add(new UserBadge { UserId = userId, BadgeId = badge.Id });
            awarded.Add(badge);
        }
        if (awarded.Count > 0) await db.SaveChangesAsync();
        return awarded;
    }

    public async Task<List<(Badge Badge, bool Owned, DateTime? AwardedAt)>> GetUserBadgesAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.Badges.AsNoTracking().ToListAsync();
        var owned = await db.UserBadges.AsNoTracking().Where(b => b.UserId == userId).ToListAsync();
        return all.Select(b =>
        {
            var ub = owned.FirstOrDefault(o => o.BadgeId == b.Id);
            return (b, ub is not null, ub?.AwardedAt);
        }).ToList();
    }
}
