using HajjVR.Data;
using HajjVR.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;

namespace HajjVR.Services;

public record SearchHit(string Title, string Snippet, string Source, double Score);

/// <summary>
/// Pencarian semantik dengan Microsoft.Extensions.VectorData (InMemory vector store).
/// Bila embedding provider tidak dikonfigurasi, otomatis fallback ke pencarian kata kunci berbobot
/// sehingga fitur tetap berfungsi tanpa API key.
/// </summary>
public class SemanticSearchService(IDbContextFactory<AppDbContext> dbFactory, IServiceProvider services)
{
    public class DocRecord
    {
        [VectorStoreKey] public string Id { get; set; } = "";
        [VectorStoreData] public string Title { get; set; } = "";
        [VectorStoreData] public string Text { get; set; } = "";
        [VectorStoreData] public string Source { get; set; } = "";
        [VectorStoreVector(1536)] public ReadOnlyMemory<float> Embedding { get; set; }
    }

    private readonly SemaphoreSlim _lock = new(1, 1);
    private VectorStoreCollection<string, DocRecord>? _collection;
    private IEmbeddingGenerator<string, Embedding<float>>? _embedder;
    private bool _vectorReady;
    private List<(string Title, string Text, string Source)> _corpus = [];
    private DateTime _indexedAt = DateTime.MinValue;

    public bool UsingVectors => _vectorReady;

    private async Task<List<(string Title, string Text, string Source)>> LoadCorpusAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var corpus = new List<(string, string, string)>();
        foreach (var d in await db.Documents.AsNoTracking().Where(d => d.ContentText != "").ToListAsync())
            corpus.Add((d.Title, d.ContentText, d.Kind));
        foreach (var l in await db.Locations.AsNoTracking().ToListAsync())
            corpus.Add(($"{l.Name} ({l.NameArabic})", l.Description, "Lokasi"));
        return corpus;
    }

    /// <summary>Bangun ulang indeks (dipanggil lazy, di-refresh tiap 10 menit).</summary>
    private async Task EnsureIndexAsync()
    {
        if (DateTime.UtcNow - _indexedAt < TimeSpan.FromMinutes(10)) return;
        await _lock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _indexedAt < TimeSpan.FromMinutes(10)) return;
            _corpus = await LoadCorpusAsync();
            _vectorReady = false;

            var factory = services.GetRequiredService<KernelFactory>();
            _embedder = factory.CreateEmbeddingGenerator();
            if (_embedder is not null)
            {
                try
                {
                    var store = new InMemoryVectorStore();
                    var col = store.GetCollection<string, DocRecord>("docs");
                    await col.EnsureCollectionExistsAsync();
                    int i = 0;
                    foreach (var (title, text, source) in _corpus)
                    {
                        var emb = await _embedder.GenerateVectorAsync($"{title}\n{text}");
                        await col.UpsertAsync(new DocRecord { Id = $"doc-{i++}", Title = title, Text = text, Source = source, Embedding = emb });
                    }
                    _collection = col;
                    _vectorReady = true;
                }
                catch
                {
                    _vectorReady = false; // fallback keyword
                }
            }
            _indexedAt = DateTime.UtcNow;
        }
        finally { _lock.Release(); }
    }

    public void Invalidate() => _indexedAt = DateTime.MinValue;

    public async Task<List<SearchHit>> SearchAsync(string query, int top = 5)
    {
        await EnsureIndexAsync();

        if (_vectorReady && _collection is not null && _embedder is not null)
        {
            try
            {
                var qv = await _embedder.GenerateVectorAsync(query);
                var hits = new List<SearchHit>();
                await foreach (var r in _collection.SearchAsync(qv, top))
                    hits.Add(new SearchHit(r.Record.Title, Snip(r.Record.Text), r.Record.Source, r.Score ?? 0));
                if (hits.Count > 0) return hits;
            }
            catch { /* jatuh ke keyword */ }
        }
        return KeywordSearch(query, top);
    }

    private List<SearchHit> KeywordSearch(string query, int top)
    {
        var terms = query.ToLowerInvariant()
            .Split([' ', ',', '?', '!', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2).ToArray();
        if (terms.Length == 0) return [];

        return _corpus.Select(c =>
            {
                var title = c.Title.ToLowerInvariant();
                var text = c.Text.ToLowerInvariant();
                double score = terms.Sum(t =>
                    (title.Contains(t) ? 3.0 : 0) +
                    System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(t)).Count * 0.5);
                return (c, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(top)
            .Select(x => new SearchHit(x.c.Title, Snip(x.c.Text), x.c.Source, Math.Round(x.score, 2)))
            .ToList();
    }

    private static string Snip(string text)
    {
        text = text.Trim();
        return text.Length > 420 ? text[..420] + "…" : text;
    }
}
