using System.Net.Http;
using Copilot.Core;
namespace Copilot.App;
public sealed class Services
{
    public LocalSettings Settings { get; } = new();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(45) };
    public ProviderRegistry Registry { get; }
    public Diagnostics Log { get; } = new(LocalSettings.Root);
    public event Action<int>? ModelChanged;
    public Services() { Registry = new(Http); Log.ContentEnabled = Settings.Value.DebugContentLogging; Log.Redactor = text => { foreach (var provider in new[] { "Deepgram", "OpenRouter", "OpenAI" }) { try { var key = Settings.Get(provider); if (!string.IsNullOrEmpty(key)) text = text.Replace(key, "[REDACTED]", StringComparison.Ordinal); } catch { } } return text; }; }
    public void ConfigurationChanged(int box) => ModelChanged?.Invoke(box);
}
