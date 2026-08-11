using System.Text;
using System.Text.Json;
using HajjVR.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace HajjVR.Services.Ai;

public record ChatAttachment(string Url, string Name, string ContentType, bool IsImage);

/// <summary>Orkestrasi percakapan Haji Sule: sesi, riwayat, attachment, function calling, streaming.</summary>
public class ChatAiService(IDbContextFactory<AppDbContext> dbFactory, KernelFactory kernelFactory, NavigationManagerAccessor nav)
{
    // ---------- Session management ----------
    public async Task<List<ChatSession>> GetSessionsAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ChatSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt).ToListAsync();
    }

    public async Task<ChatSession> CreateSessionAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var session = new ChatSession { UserId = userId };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.ChatSessions.FindAsync(sessionId);
        if (s is not null) { db.ChatSessions.Remove(s); await db.SaveChangesAsync(); }
    }

    /// <summary>Reset: hapus semua pesan tapi pertahankan sesi.</summary>
    public async Task ResetSessionAsync(string sessionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ChatMessages.Where(m => m.SessionId == sessionId).ExecuteDeleteAsync();
        var s = await db.ChatSessions.FindAsync(sessionId);
        if (s is not null) { s.Title = "Percakapan Baru"; s.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
    }

    public async Task<List<ChatMessageEntity>> GetMessagesAsync(string sessionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId).OrderBy(m => m.Id).ToListAsync();
    }

    // ---------- Chat ----------
    /// <summary>Kirim pesan user dan stream jawaban asisten. onDelta dipanggil setiap potongan teks.</summary>
    public async Task<string> SendAsync(string sessionId, string userText, List<ChatAttachment> attachments,
        Action<string>? onDelta = null, CancellationToken ct = default)
    {
        var cfg = kernelFactory.GetConfig();

        // Simpan pesan user
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.ChatMessages.Add(new ChatMessageEntity
            {
                SessionId = sessionId,
                Role = "user",
                Content = userText,
                AttachmentsJson = attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : null
            });
            var session = await db.ChatSessions.FindAsync(sessionId);
            if (session is not null)
            {
                if (session.Title == "Percakapan Baru" && userText.Length > 0)
                    session.Title = userText.Length > 60 ? userText[..60] + "…" : userText;
                session.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
        }

        // Bangun riwayat percakapan
        var history = new ChatHistory(cfg.SystemPrompt);
        var messages = await GetMessagesAsync(sessionId);
        foreach (var m in messages)
        {
            if (m.Role == "assistant")
            {
                history.AddAssistantMessage(m.Content);
                continue;
            }
            var items = new ChatMessageContentItemCollection();
            var text = m.Content;
            if (!string.IsNullOrEmpty(m.AttachmentsJson))
            {
                var atts = JsonSerializer.Deserialize<List<ChatAttachment>>(m.AttachmentsJson) ?? [];
                foreach (var a in atts)
                {
                    if (a.IsImage)
                        items.Add(new ImageContent(new Uri(ToAbsolute(a.Url))));
                    else
                        text += $"\n\n[Lampiran dokumen: {a.Name}]({ToAbsolute(a.Url)})";
                }
            }
            items.Insert(0, new TextContent(text));
            history.AddUserMessage(items);
        }

        var kernel = kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = cfg.Temperature,
            MaxTokens = cfg.MaxTokens,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct))
            {
                if (chunk.Content is { Length: > 0 } delta)
                {
                    sb.Append(delta);
                    onDelta?.Invoke(delta);
                }
            }
        }
        catch (Exception ex)
        {
            var msg = $"⚠️ Maaf, terjadi kesalahan saat menghubungi model **{cfg.Provider}/{cfg.Model}**: `{ex.Message}`\n\n" +
                      "Periksa API key & pengaturan LLM di halaman **Pengaturan**.";
            sb.Clear();
            sb.Append(msg);
            onDelta?.Invoke(msg);
        }

        var answer = sb.ToString();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.ChatMessages.Add(new ChatMessageEntity { SessionId = sessionId, Role = "assistant", Content = answer });
            await db.SaveChangesAsync();
        }
        return answer;
    }

    /// <summary>URL relatif (storage FileSystem) perlu jadi absolut agar bisa diakses penyedia LLM.</summary>
    private string ToAbsolute(string url)
        => url.StartsWith('/') ? $"{nav.BaseUri.TrimEnd('/')}{url}" : url;
}

/// <summary>Menyimpan BaseUri aplikasi (di-set dari komponen) agar service non-UI bisa membentuk URL absolut.</summary>
public class NavigationManagerAccessor
{
    public string BaseUri { get; set; } = "http://localhost:5000/";
}
