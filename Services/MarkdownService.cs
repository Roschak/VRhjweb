using Markdig;

namespace HajjVR.Services;

/// <summary>Render Markdown → HTML untuk thread chat (tabel, media, kode, task list, dll).</summary>
public static class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()      // tabel pipe, footnote, task list, auto-link, dll
        .UseMediaLinks()              // ![..](youtube/mp4/mp3) → embed <video>/<audio>/<iframe>
        .UseEmojiAndSmiley()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()                // cegah injeksi HTML mentah dari model/user
        .Build();

    public static string ToHtml(string markdown)
        => string.IsNullOrWhiteSpace(markdown) ? "" : Markdown.ToHtml(markdown, Pipeline);
}
