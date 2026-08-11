using HajjVR.Data;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Services;

/// <summary>
/// Simulasi keramaian real-time: tiap menit menulis snapshot baru per zona
/// (random-walk di sekitar pola waktu shalat) untuk dashboard & heatmap.
/// </summary>
public class CrowdSimulatorService(IDbContextFactory<AppDbContext> dbFactory, ILogger<CrowdSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Zones = ["mataf", "masaa", "mosque", "arafah", "muzdalifah", "mina", "nabawi", "raudhah"];
    private readonly Random _rnd = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // beri waktu startup + seed selesai
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var now = DateTime.UtcNow;
                foreach (var zone in Zones)
                {
                    var last = await db.CrowdSnapshots.Where(c => c.Zone == zone)
                        .OrderByDescending(c => c.Timestamp).FirstOrDefaultAsync(ct);
                    int baseline = last?.Count ?? 10000;
                    int next = Math.Max(100, baseline + _rnd.Next(-baseline / 12, baseline / 12));
                    db.CrowdSnapshots.Add(new CrowdSnapshot { Zone = zone, Count = next, Timestamp = now });
                }
                // pangkas data lebih tua dari 7 hari agar DB tetap ringan
                await db.CrowdSnapshots.Where(c => c.Timestamp < now.AddDays(-7)).ExecuteDeleteAsync(ct);
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CrowdSimulator gagal menulis snapshot");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
