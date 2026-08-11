using System.Collections.Concurrent;
using HajjVR.Data;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Services;

/// <summary>
/// Sumber konfigurasi terpadu: nilai dari appsettings.json dapat di-override lewat UI
/// (tersimpan di tabel AppSettings). Override DB menang atas appsettings.
/// </summary>
public class SettingsService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration config)
{
    private readonly ConcurrentDictionary<string, string?> _cache = new();
    private volatile bool _loaded;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_cache)
        {
            if (_loaded) return;
            using var db = dbFactory.CreateDbContext();
            foreach (var s in db.AppSettings.AsNoTracking().ToList())
                _cache[s.Key] = s.Value;
            _loaded = true;
        }
    }

    /// <summary>Ambil nilai setting. Key pakai notasi ':' seperti IConfiguration (mis. "Llm:Provider").</summary>
    public string? Get(string key)
    {
        EnsureLoaded();
        if (_cache.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return config[key];
    }

    public string Get(string key, string fallback) => Get(key) is { Length: > 0 } v ? v : fallback;

    public double GetDouble(string key, double fallback)
        => double.TryParse(Get(key), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback)
        => bool.TryParse(Get(key), out var v) ? v : fallback;

    /// <summary>Simpan override ke database (dipakai halaman Pengaturan).</summary>
    public async Task SetAsync(string key, string value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.AppSettings.FindAsync(key);
        if (existing is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else existing.Value = value;
        await db.SaveChangesAsync();
        _cache[key] = value;
    }

    public async Task ResetAsync(string key)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.AppSettings.FindAsync(key);
        if (existing is not null) { db.AppSettings.Remove(existing); await db.SaveChangesAsync(); }
        _cache.TryRemove(key, out _);
    }

    /// <summary>Semua key yang di-override lewat UI.</summary>
    public IReadOnlyDictionary<string, string?> Overrides { get { EnsureLoaded(); return _cache; } }
}
