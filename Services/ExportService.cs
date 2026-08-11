using ClosedXML.Excel;
using HajjVR.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HajjVR.Services;

/// <summary>Ekspor laporan perjalanan jamaah ke Excel (ClosedXML) dan PDF (QuestPDF).</summary>
public class ExportService(IDbContextFactory<AppDbContext> dbFactory)
{
    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<byte[]> ExportProgressExcelAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Role == Roles.Jamaah).OrderBy(u => u.DisplayName).ToListAsync();
        var progress = await db.RitualProgresses.AsNoTracking().ToListAsync();
        var rituals = Enum.GetValues<RitualType>();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Progres Jamaah");
        ws.Cell(1, 1).Value = "Laporan Progres Ibadah Jamaah — HajjVR";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"Dibuat: {DateTime.Now:dd MMMM yyyy HH:mm}";

        int headerRow = 4;
        ws.Cell(headerRow, 1).Value = "Nama";
        ws.Cell(headerRow, 2).Value = "Rombongan";
        ws.Cell(headerRow, 3).Value = "Paket";
        int col = 4;
        foreach (var r in rituals) ws.Cell(headerRow, col++).Value = AnalyticsService.RitualName(r);
        ws.Cell(headerRow, col).Value = "% Selesai";
        var header = ws.Range(headerRow, 1, headerRow, col);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#1B5E20")).Font.SetFontColor(XLColor.White);

        int row = headerRow + 1;
        foreach (var u in users)
        {
            ws.Cell(row, 1).Value = u.DisplayName;
            ws.Cell(row, 2).Value = u.Profile?.GroupName ?? "-";
            ws.Cell(row, 3).Value = u.Profile?.PackageType ?? "-";
            col = 4;
            int done = 0;
            foreach (var r in rituals)
            {
                var p = progress.FirstOrDefault(x => x.UserId == u.Id && x.Ritual == r);
                var status = p?.Status ?? ProgressStatus.NotStarted;
                if (status == ProgressStatus.Completed) done++;
                var cell = ws.Cell(row, col++);
                cell.Value = status switch
                {
                    ProgressStatus.Completed => "Selesai",
                    ProgressStatus.InProgress => "Berjalan",
                    _ => "-"
                };
                if (status == ProgressStatus.Completed) cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#C8E6C9"));
                else if (status == ProgressStatus.InProgress) cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF9C4"));
            }
            ws.Cell(row, col).Value = Math.Round(100.0 * done / rituals.Length, 1);
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportProgressPdfAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Role == Roles.Jamaah).OrderBy(u => u.DisplayName).ToListAsync();
        var progress = await db.RitualProgresses.AsNoTracking().ToListAsync();
        int totalRituals = Enum.GetValues<RitualType>().Length;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));
                page.Header().Column(c =>
                {
                    c.Item().Text("🕋 HajjVR — Laporan Perjalanan Jamaah").FontSize(16).Bold().FontColor("#1B5E20");
                    c.Item().Text($"Dibuat {DateTime.Now:dd MMMM yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    c.Item().PaddingTop(4).LineHorizontal(2).LineColor("#1B5E20");
                });
                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var t in new[] { "Nama Jamaah", "Rombongan", "Paket", "Ritual Selesai", "Progres" })
                            h.Cell().Background("#1B5E20").Padding(5).Text(t).FontColor(Colors.White).Bold();
                    });
                    bool odd = false;
                    foreach (var u in users)
                    {
                        int done = progress.Count(p => p.UserId == u.Id && p.Status == ProgressStatus.Completed);
                        var bg = odd ? "#F1F8E9" : "#FFFFFF";
                        odd = !odd;
                        table.Cell().Background(bg).Padding(5).Text(u.DisplayName);
                        table.Cell().Background(bg).Padding(5).Text(u.Profile?.GroupName ?? "-");
                        table.Cell().Background(bg).Padding(5).Text(u.Profile?.PackageType ?? "-");
                        table.Cell().Background(bg).Padding(5).Text($"{done} / {totalRituals}");
                        table.Cell().Background(bg).Padding(5).Text($"{100.0 * done / totalRituals:0}%");
                    }
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Halaman ");
                    t.CurrentPageNumber();
                    t.Span(" dari ");
                    t.TotalPages();
                });
            });
        });
        return doc.GeneratePdf();
    }

    /// <summary>Laporan PDF detail satu jamaah.</summary>
    public async Task<byte[]> ExportJamaahPdfAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().Include(u => u.Profile).FirstAsync(u => u.Id == userId);
        var progress = await db.RitualProgresses.AsNoTracking()
            .Where(p => p.UserId == userId).OrderBy(p => p.Ritual).ToListAsync();
        var badges = await db.UserBadges.AsNoTracking().Include(b => b.Badge)
            .Where(b => b.UserId == userId).ToListAsync();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));
                page.Header().Text($"🕋 Laporan Perjalanan — {user.DisplayName}").FontSize(15).Bold().FontColor("#1B5E20");
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text($"Rombongan: {user.Profile?.GroupName ?? "-"}   •   Paket: {user.Profile?.PackageType ?? "-"}   •   Paspor: {user.Profile?.PassportNumber ?? "-"}");
                    col.Item().PaddingTop(10).Text("Progres Ritual").Bold().FontSize(12);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(3); c.RelativeColumn(2); });
                        table.Header(h =>
                        {
                            foreach (var t in new[] { "Ritual", "Status", "Selesai Pada", "Durasi (menit)" })
                                h.Cell().Background("#1B5E20").Padding(4).Text(t).FontColor(Colors.White).Bold();
                        });
                        foreach (var p in progress)
                        {
                            table.Cell().Padding(4).Text(AnalyticsService.RitualName(p.Ritual));
                            table.Cell().Padding(4).Text(p.Status switch
                            {
                                ProgressStatus.Completed => "✔ Selesai",
                                ProgressStatus.InProgress => "◐ Berjalan",
                                _ => "— Belum"
                            });
                            table.Cell().Padding(4).Text(p.CompletedAt?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "-");
                            table.Cell().Padding(4).Text(p.DurationMinutes > 0 ? p.DurationMinutes.ToString() : "-");
                        }
                    });
                    col.Item().PaddingTop(10).Text("Badge Diraih").Bold().FontSize(12);
                    col.Item().Text(badges.Count == 0
                        ? "Belum ada badge."
                        : string.Join("   ", badges.Select(b => $"{b.Badge?.Icon} {b.Badge?.Name} (+{b.Badge?.Points})")));
                });
                page.Footer().AlignCenter().Text($"HajjVR • {DateTime.Now:dd MMMM yyyy}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
        return doc.GeneratePdf();
    }
}
