using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HajjVR.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

namespace HajjVR.Services.Ai;

// ====================== WAKTU ======================
public class TimePlugin
{
    [KernelFunction("tanggal_sekarang"), Description("Tanggal dan waktu saat ini (Masehi & Hijriah), termasuk nama hari.")]
    public string Now()
    {
        var now = DateTime.Now;
        var hijri = new UmAlQuraCalendar();
        string[] hijriMonths = ["Muharram", "Safar", "Rabiul Awal", "Rabiul Akhir", "Jumadil Awal", "Jumadil Akhir",
            "Rajab", "Sya'ban", "Ramadhan", "Syawal", "Dzulqa'dah", "Dzulhijjah"];
        var id = new CultureInfo("id-ID");
        return $"Sekarang: {now.ToString("dddd, dd MMMM yyyy HH:mm:ss", id)} WIB(server) | " +
               $"Hijriah: {hijri.GetDayOfMonth(now)} {hijriMonths[hijri.GetMonth(now) - 1]} {hijri.GetYear(now)} H | UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
    }

    [KernelFunction("selisih_hari"), Description("Hitung selisih hari antara dua tanggal (format yyyy-MM-dd).")]
    public string DaysBetween(string tanggalAwal, string tanggalAkhir)
    {
        var a = DateTime.Parse(tanggalAwal, CultureInfo.InvariantCulture);
        var b = DateTime.Parse(tanggalAkhir, CultureInfo.InvariantCulture);
        return $"{(b - a).TotalDays:0} hari";
    }

    [KernelFunction("konversi_zona_waktu"), Description("Waktu saat ini di zona waktu tertentu (contoh id zona: 'Arab Standard Time', 'SE Asia Standard Time').")]
    public string TimeInZone(string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("dddd, dd MMMM yyyy HH:mm", new CultureInfo("id-ID"));
    }
}

// ====================== MATEMATIKA ======================
public class MathPlugin
{
    [KernelFunction("hitung"), Description("Evaluasi ekspresi matematika. Mendukung + - * / % ( ). Contoh: (25000000*40)/12")]
    public string Calculate([Description("Ekspresi matematika, mis. 2*(3+4)")] string expression)
    {
        var result = new DataTable().Compute(expression, null);
        return Convert.ToDouble(result).ToString("G15", CultureInfo.InvariantCulture);
    }

    [KernelFunction("konversi_mata_uang_kasar"), Description("Konversi kasar SAR/USD ke IDR dengan kurs tetap perkiraan (bukan kurs real-time).")]
    public string ConvertCurrency(double jumlah, [Description("SAR, USD, atau IDR")] string dari, [Description("SAR, USD, atau IDR")] string ke)
    {
        var toIdr = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["SAR"] = 4300, ["USD"] = 16200, ["IDR"] = 1 };
        if (!toIdr.TryGetValue(dari, out var d) || !toIdr.TryGetValue(ke, out var k)) return "Mata uang tidak dikenal (SAR/USD/IDR).";
        return $"{jumlah:n2} {dari.ToUpper()} ≈ {jumlah * d / k:n2} {ke.ToUpper()} (kurs perkiraan, bukan real-time)";
    }
}

// ====================== WEB ======================
public class WebPlugin(IHttpClientFactory httpFactory, SettingsService settings)
{
    [KernelFunction("cari_internet"), Description("Cari informasi terbaru di internet menggunakan Tavily. Gunakan untuk berita, harga, jadwal, atau info yang tidak ada di database.")]
    public async Task<string> TavilySearch([Description("Kata kunci pencarian")] string query)
    {
        var apiKey = settings.Get("Tavily:ApiKey");
        if (string.IsNullOrEmpty(apiKey))
            return "Pencarian internet belum aktif: Tavily:ApiKey belum diisi di Pengaturan.";
        var http = httpFactory.CreateClient("ai");
        var resp = await http.PostAsJsonAsync("https://api.tavily.com/search", new
        {
            api_key = apiKey,
            query,
            max_results = 5,
            include_answer = true
        });
        if (!resp.IsSuccessStatusCode) return $"Tavily error: {resp.StatusCode}";
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var sb = new System.Text.StringBuilder();
        if (json.TryGetProperty("answer", out var ans)) sb.AppendLine($"Ringkasan: {ans.GetString()}\n");
        if (json.TryGetProperty("results", out var results))
            foreach (var r in results.EnumerateArray())
                sb.AppendLine($"- {r.GetProperty("title").GetString()} — {r.GetProperty("url").GetString()}\n  {r.GetProperty("content").GetString()?[..Math.Min(300, r.GetProperty("content").GetString()!.Length)]}");
        return sb.ToString();
    }

    [KernelFunction("baca_halaman_web"), Description("Ambil dan baca isi teks sebuah halaman web dari URL.")]
    public async Task<string> ScrapePage([Description("URL halaman")] string url)
    {
        var http = httpFactory.CreateClient("ai");
        var html = await http.GetStringAsync(url);
        // buang script/style lalu strip tag
        html = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(Regex.Replace(text, @"\s+", " ")).Trim();
        return text.Length > 6000 ? text[..6000] + " …(terpotong)" : text;
    }

    [KernelFunction("baca_file_url"), Description("Unduh dan baca isi file teks (txt, md, csv, json) dari sebuah URL.")]
    public async Task<string> ReadFileFromUrl([Description("URL file")] string url)
    {
        var http = httpFactory.CreateClient("ai");
        var content = await http.GetStringAsync(url);
        return content.Length > 8000 ? content[..8000] + " …(terpotong)" : content;
    }
}

// ====================== DATA (query database aplikasi) ======================
public class DataPlugin(IDbContextFactory<AppDbContext> dbFactory, SemanticSearchService search)
{
    [KernelFunction("statistik_jamaah"), Description("Statistik agregat: jumlah jamaah, pembimbing, progres rata-rata, jumlah umrah/haji selesai.")]
    public async Task<string> JamaahStats()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var total = await db.Users.CountAsync(u => u.Role == Roles.Jamaah);
        var pembimbing = await db.Users.CountAsync(u => u.Role == Roles.Pembimbing);
        var progress = await db.RitualProgresses.ToListAsync();
        var done = progress.Count(p => p.Status == ProgressStatus.Completed);
        return $"Jumlah jamaah: {total}. Pembimbing: {pembimbing}. " +
               $"Total item ritual selesai: {done} dari {progress.Count} ({(progress.Count > 0 ? 100.0 * done / progress.Count : 0):0.0}%).";
    }

    [KernelFunction("cari_jamaah"), Description("Cari data jamaah berdasarkan nama. Mengembalikan nama, rombongan, paket, dan progres.")]
    public async Task<string> FindJamaah([Description("Sebagian nama jamaah")] string nama)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.Include(u => u.Profile)
            .Where(u => u.Role == Roles.Jamaah && u.DisplayName.ToLower().Contains(nama.ToLower()))
            .Take(5).ToListAsync();
        if (users.Count == 0) return $"Tidak ada jamaah dengan nama mengandung '{nama}'.";
        var lines = new List<string>();
        foreach (var u in users)
        {
            var done = await db.RitualProgresses.CountAsync(p => p.UserId == u.Id && p.Status == ProgressStatus.Completed);
            var total = Enum.GetValues<RitualType>().Length;
            lines.Add($"- {u.DisplayName} | Rombongan: {u.Profile?.GroupName ?? "-"} | Paket: {u.Profile?.PackageType} | Progres: {done}/{total} ritual");
        }
        return string.Join("\n", lines);
    }

    [KernelFunction("keramaian_lokasi"), Description("Data keramaian (jumlah orang) terkini per zona: mataf, masaa, mosque, arafah, muzdalifah, mina, nabawi, raudhah.")]
    public async Task<string> CrowdNow()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var snaps = await db.CrowdSnapshots
            .GroupBy(s => s.Zone)
            .Select(g => g.OrderByDescending(x => x.Timestamp).First())
            .ToListAsync();
        return string.Join("\n", snaps.OrderByDescending(s => s.Count)
            .Select(s => $"- {AnalyticsService.ZoneLabel(s.Zone)}: ±{s.Count:n0} orang (per {s.Timestamp:HH:mm} UTC)"));
    }

    [KernelFunction("info_lokasi"), Description("Informasi lokasi suci (Ka'bah, Safa, Marwah, Arafah, Raudhah, dll) dari database, termasuk koordinat & deskripsi.")]
    public async Task<string> LocationInfo([Description("Nama lokasi")] string nama)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var locs = await db.Locations
            .Where(l => l.Name.ToLower().Contains(nama.ToLower()))
            .Take(3).ToListAsync();
        if (locs.Count == 0) return $"Lokasi '{nama}' tidak ditemukan di database.";
        return string.Join("\n\n", locs.Select(l =>
            $"**{l.Name}** ({l.NameArabic}) — kategori {l.Category}\n{l.Description}\nKoordinat: {l.Latitude}, {l.Longitude}"));
    }

    [KernelFunction("papan_peringkat"), Description("Papan peringkat (leaderboard) jamaah teratas berdasarkan poin gamifikasi.")]
    public async Task<string> Leaderboard()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var badges = await db.UserBadges.Include(b => b.Badge).Include(b => b.User).ToListAsync();
        var completed = await db.RitualProgresses.Where(p => p.Status == ProgressStatus.Completed).ToListAsync();
        var users = await db.Users.Where(u => u.Role == Roles.Jamaah).ToListAsync();
        var top = users.Select(u => new
        {
            u.DisplayName,
            Points = badges.Where(b => b.UserId == u.Id).Sum(b => b.Badge?.Points ?? 0)
                     + completed.Count(p => p.UserId == u.Id) * 10
        }).OrderByDescending(x => x.Points).Take(10).ToList();
        return string.Join("\n", top.Select((x, i) => $"{i + 1}. {x.DisplayName} — {x.Points} poin"));
    }

    [KernelFunction("cari_panduan_manasik"), Description("Cari panduan manasik/dokumen pengetahuan (thawaf, sa'i, ihram, wukuf, jumrah, dll) dengan pencarian semantik.")]
    public async Task<string> SearchGuides([Description("Pertanyaan atau kata kunci")] string query)
    {
        var results = await search.SearchAsync(query, 3);
        if (results.Count == 0) return "Tidak ditemukan panduan yang relevan.";
        return string.Join("\n\n---\n\n", results.Select(r => $"### {r.Title}\n{r.Snippet}"));
    }
}
