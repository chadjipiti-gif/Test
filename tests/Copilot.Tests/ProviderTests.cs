using System.Net;
using Copilot.Core;
using Xunit;
namespace Copilot.Tests;
public class ProviderTests
{
    private sealed class Keys : IKeyStore { public string? Get(string p) => "test-only-placeholder"; public void Save(string p, string k) { } }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request, ct); }
    private static Diagnostics Log() => new(Path.Combine(Path.GetTempPath(), "copilot-tests", Guid.NewGuid().ToString()));
    [Fact]
    public async Task OpenRouterOmitsUnsupportedParametersAndParsesCompleteResponse()
    {
        var http = new HttpClient(new Handler(async (req, ct) => { Assert.Equal("https://openrouter.ai/api/v1/chat/completions", req.RequestUri!.AbsoluteUri); Assert.Equal("Bearer", req.Headers.Authorization!.Scheme); var body = await req.Content!.ReadAsStringAsync(ct); Assert.Contains("max_tokens", body); Assert.DoesNotContain("temperature", body); Assert.DoesNotContain("top_p", body); Assert.DoesNotContain("reasoning", body); Assert.Contains("canonical transcript", body); return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Could we clarify the deadline?\"}}]}") }; }));
        var result = await new OpenRouterProvider(http, new Keys(), Log()).GenerateAsync("canonical transcript", new(), new("test", "test", "OpenRouter", Capability.Text, false, ["max_tokens"]), CancellationToken.None); Assert.Equal("Could we clarify the deadline?", result);
    }
    [Fact]
    public async Task CatalogFiltersNonTextAndPreservesSupportedParameters()
    {
        var registry = new ProviderRegistry(new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"verified-model\",\"name\":\"Verified\",\"architecture\":{\"output_modalities\":[\"text\"]},\"supported_parameters\":[\"max_tokens\",\"reasoning\"]},{\"id\":\"image-only\",\"name\":\"Image\",\"architecture\":{\"output_modalities\":[\"image\"]}}]}") })))); await registry.RefreshOpenRouterAsync(CancellationToken.None); Assert.NotNull(registry.Find("verified-model")); Assert.Contains("reasoning", registry.Find("verified-model")!.Parameters); Assert.Null(registry.Find("image-only")); Assert.NotNull(registry.Find("nova-3"));
    }
    [Fact]
    public async Task ProviderFailureDoesNotPreventOtherBoxResult()
    {
        var registry = new ProviderRegistry(new HttpClient()); registry.Models.Add(new("test", "test", "OpenRouter", Capability.Text, false, [])); var log = Log(); using var orchestration = new SuggestionOrchestrator(registry, new(new KeywordRetriever(), log), new MixedProvider(), log); var results = new List<SuggestionResult>(); orchestration.Result += results.Add; var now = DateTimeOffset.UtcNow;
        await orchestration.CommitAsync(new(Guid.NewGuid(), new(1, []), new(1, []), new(), new("", "", "", [], [], [], [], [], [], [], ""), now, now, ""), [new() { Model = "test", MaxTokens = 1 }, new() { Model = "test", MaxTokens = 2 }, new()]); Assert.Contains(results, r => r.Id.Box == "A" && r.State.Contains("error")); Assert.Contains(results, r => r.Id.Box == "B" && r.Text == "B works");
    }
    private sealed class MixedProvider : ISuggestionProvider { public Task<string> GenerateAsync(string p, BoxConfig c, ModelEntry m, CancellationToken ct) => c.MaxTokens == 1 ? Task.FromException<string>(new HttpRequestException("test")) : Task.FromResult("B works"); }
    [Fact]
    public void DeepgramFinalDiarizationKeepsPerSpeakerWordsAndTimes()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"type":"Results","start":0,"duration":2,"is_final":true,"speech_final":true,"channel":{"alternatives":[{"transcript":"Hello. Yes.","confidence":0.98,"words":[{"word":"hello","punctuated_word":"Hello.","start":0,"end":0.5,"speaker":0},{"word":"yes","punctuated_word":"Yes.","start":1,"end":1.5,"speaker":1}]}]}}""");
        var segments = DeepgramParser.Parse(doc.RootElement, "MIXED", DateTimeOffset.UnixEpoch); Assert.Equal(2, segments.Count); Assert.Equal("0", segments[0].SpeakerId); Assert.Equal("1", segments[1].SpeakerId); Assert.Equal("Hello.", segments[0].Text); Assert.All(segments, s => { Assert.True(s.IsFinal); Assert.Equal(SpeakerRole.Unknown, s.Role); Assert.Equal(.98, s.Confidence); }); Assert.Equal(TimeSpan.FromSeconds(1), segments[1].Start - segments[0].Start);
    }
    [Fact]
    public void DeepgramInterimUsesDeterministicClearRole()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"start":0,"duration":1,"is_final":false,"channel":{"alternatives":[{"transcript":"Could you","confidence":0.9}]}}"""); var segments = DeepgramParser.Parse(doc.RootElement, "ME", DateTimeOffset.UnixEpoch); Assert.Single(segments); Assert.False(segments[0].IsFinal); Assert.Equal(SpeakerRole.Me, segments[0].Role);
    }
    [Fact] public void PercentilesUseCompletedSamples() { var log = Log(); foreach (var ms in new[] { 10d, 20, 30, 40, 50, 60, 70, 80, 90, 100 }) log.Latency("A", ms); var p = log.Percentiles("A"); Assert.Equal(50, p.P50); Assert.Equal(100, p.P95); Assert.Equal(100, p.P99); }
}
