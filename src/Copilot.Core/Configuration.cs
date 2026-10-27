using System.Net.Http.Headers;
using System.Text.Json;
namespace Copilot.Core;
public enum Capability { Stt, Text, Realtime }
public record ModelEntry(string Id, string DisplayName, string Provider, Capability Capability, bool Streaming, string[] Parameters, bool Available = true)
{
    public string RequiredCredential => Provider;
    public string Route => Provider switch { "OpenRouter" => "https://openrouter.ai/api/v1/chat/completions", "Deepgram" => "wss://api.deepgram.com/v1/listen", "OpenAI" => "wss://api.openai.com/v1/realtime", _ => "" };
    public bool Enabled => Available;
    public override string ToString() => DisplayName;
}
public class BoxConfig
{
    public string? Model { get; set; }
    public double Temperature { get; set; } = 0.3;
    public double TopP { get; set; } = 1;
    public int MaxTokens { get; set; } = 180;
    public string ReasoningEffort { get; set; } = "low";
    public int TranscriptBudget { get; set; } = 2500;
    public int ContextBudget { get; set; } = 900;
}
public record StaticSummary(string WhoMe = "", string WhoThem = "", string Discussion = "", string Goal = "");
public class UserSettings
{
    public BoxConfig[] Boxes { get; set; } = [new(), new() { TranscriptBudget = 4500 }, new()];
    public string Notes { get; set; } = "";
    public StaticSummary Summary { get; set; } = new();
    public bool Interim { get; set; } = true;
    public bool Diarize { get; set; } = true;
    public bool SmartFormat { get; set; } = true;
    public string Keyterms { get; set; } = "";
    public int EndpointMs { get; set; } = 300;
    public int PauseMs { get; set; } = 900;
    public int FallbackMs { get; set; } = 3500;
    public bool DebugContentLogging { get; set; }
    public string? MicrophoneId { get; set; }
    public string? OutputId { get; set; }
    public bool Infer { get; set; }
}
public interface IKeyStore { string? Get(string provider); void Save(string provider, string key); }
public sealed class ProviderRegistry(HttpClient http)
{
    public static ModelEntry None { get; } = new("", "None", "", Capability.Text, false, []);
    public List<ModelEntry> Models { get; } = [new("nova-3", "Deepgram Nova-3", "Deepgram", Capability.Stt, true, ["interim_results", "diarize", "smart_format", "endpointing", "keyterm"]), new("gpt-realtime", "OpenAI Realtime", "OpenAI", Capability.Realtime, true, ["max_output_tokens"])];
    public async Task RefreshOpenRouterAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("https://openrouter.ai/api/v1/models", ct); res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var entries = new List<ModelEntry>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!m.TryGetProperty("architecture", out var a) || !a.TryGetProperty("output_modalities", out var o) || !o.EnumerateArray().Any(x => x.GetString() == "text")) continue;
            entries.Add(new(m.GetProperty("id").GetString()!, m.GetProperty("name").GetString()!, "OpenRouter", Capability.Text, true, m.TryGetProperty("supported_parameters", out var p) ? p.EnumerateArray().Select(x => x.GetString()!).ToArray() : []));
        }
        if (entries.Count == 0) throw new InvalidOperationException("Empty model catalog");
        Models.RemoveAll(x => x.Provider == "OpenRouter"); Models.AddRange(entries.OrderBy(x => x.DisplayName));
    }
    public ModelEntry? Find(string? id) => Models.FirstOrDefault(x => x.Id == id && x.Available);
    public async Task TestAsync(string provider, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Key missing");
        var url = provider switch { "Deepgram" => "https://api.deepgram.com/v1/projects", "OpenRouter" => "https://openrouter.ai/api/v1/key", _ => "https://api.openai.com/v1/models" };
        using var req = new HttpRequestMessage(HttpMethod.Get, url); req.Headers.Authorization = new AuthenticationHeaderValue(provider == "Deepgram" ? "Token" : "Bearer", key);
        using var response = await http.SendAsync(req, ct); response.EnsureSuccessStatusCode();
    }
}
