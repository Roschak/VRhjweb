using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using OpenAI;

namespace HajjVR.Services.Ai;

/// <summary>
/// Membangun Semantic Kernel sesuai setting "Llm:*".
/// Semua provider (OpenAI, Anthropic, Gemini, Ollama) diakses lewat endpoint OpenAI-compatible
/// sehingga cukup satu konektor:
///  - OpenAI   : https://api.openai.com/v1
///  - Anthropic: https://api.anthropic.com/v1  (OpenAI SDK compatibility)
///  - Gemini   : https://generativelanguage.googleapis.com/v1beta/openai
///  - Ollama   : http://localhost:11434/v1
/// </summary>
public class KernelFactory(SettingsService settings, IServiceProvider services)
{
    public record LlmConfig(string Provider, string Model, string ApiKey, string Endpoint, double Temperature, string SystemPrompt, int MaxTokens);

    public LlmConfig GetConfig()
    {
        var provider = settings.Get("Llm:Provider", "OpenAI");
        var endpoint = settings.Get($"Llm:{provider}:Endpoint", DefaultEndpoint(provider));
        var model = settings.Get($"Llm:{provider}:Model", DefaultModel(provider));
        var apiKey = settings.Get($"Llm:{provider}:ApiKey", "ollama");
        var temperature = settings.GetDouble("Llm:Temperature", 0.7);
        var maxTokens = (int)settings.GetDouble("Llm:MaxTokens", 2048);
        var systemPrompt = settings.Get("Llm:SystemPrompt", DefaultSystemPrompt);
        return new LlmConfig(provider, model, apiKey, endpoint, temperature, systemPrompt, maxTokens);
    }

    public static string DefaultEndpoint(string provider) => provider.ToLowerInvariant() switch
    {
        "anthropic" => "https://api.anthropic.com/v1",
        "gemini" => "https://generativelanguage.googleapis.com/v1beta/openai",
        "ollama" => "http://localhost:11434/v1",
        _ => "https://api.openai.com/v1"
    };

    public static string DefaultModel(string provider) => provider.ToLowerInvariant() switch
    {
        "anthropic" => "claude-sonnet-5",
        "gemini" => "gemini-2.5-flash",
        "ollama" => "llama3.2",
        _ => "gpt-4o-mini"
    };

    public const string DefaultSystemPrompt =
        """
        Kamu adalah 'Haji Sule', asisten virtual ramah untuk aplikasi HajjVR (Simulator Haji & Umrah).
        Gaya bicaramu hangat, sopan, sesekali humor ringan khas Sunda, dan selalu membantu.
        Tugasmu: menjawab pertanyaan seputar manasik haji/umrah, lokasi-lokasi suci, data jamaah,
        statistik aplikasi, dan informasi umum. Gunakan function/tool yang tersedia bila perlu
        (pencarian internet, kalkulasi, tanggal, query database). Jawab dalam bahasa penanya
        (default Bahasa Indonesia), gunakan format Markdown yang rapi (tabel, daftar, kode bila relevan).
        Jika tidak yakin, katakan terus terang dan sarankan bertanya ke pembimbing ibadah.
        """;

    /// <summary>Buat Kernel lengkap dengan chat completion + seluruh plugin Haji Sule.</summary>
    public Kernel CreateKernel(bool withPlugins = true)
    {
        var cfg = GetConfig();
        var builder = Kernel.CreateBuilder();
        var client = new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(cfg.ApiKey) ? "-" : cfg.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) });
        builder.AddOpenAIChatCompletion(cfg.Model, client);
        if (withPlugins)
        {
            builder.Plugins.AddFromObject(services.GetRequiredService<TimePlugin>(), "waktu");
            builder.Plugins.AddFromObject(services.GetRequiredService<MathPlugin>(), "matematika");
            builder.Plugins.AddFromObject(services.GetRequiredService<WebPlugin>(), "web");
            builder.Plugins.AddFromObject(services.GetRequiredService<DataPlugin>(), "data");
        }
        return builder.Build();
    }

    /// <summary>Generator embedding (untuk pencarian semantik). Null bila tidak dikonfigurasi.</summary>
    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator()
    {
        var provider = settings.Get("Embedding:Provider", settings.Get("Llm:Provider", "OpenAI"));
        var model = settings.Get("Embedding:Model", provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "nomic-embed-text" : "text-embedding-3-small");
        var endpoint = settings.Get("Embedding:Endpoint", DefaultEndpoint(provider));
        var apiKey = settings.Get("Embedding:ApiKey", settings.Get($"Llm:{provider}:ApiKey", ""));
        if (string.IsNullOrEmpty(apiKey) && !endpoint.Contains("localhost")) return null;
        try
        {
            var client = new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "-" : apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
            return client.GetEmbeddingClient(model).AsIEmbeddingGenerator();
        }
        catch { return null; }
    }
}
