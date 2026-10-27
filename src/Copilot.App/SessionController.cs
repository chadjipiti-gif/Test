using Copilot.Core;
namespace Copilot.App;
public sealed class SessionController(IAudioCaptureService audio, Services services) : IAsyncDisposable
{
    private CancellationTokenSource? session;
    private readonly Dictionary<string, ITranscriptionStream> streams = [];
    private readonly List<Task> running = [];
    public TranscriptState Transcript { get; } = new();
    public bool Active => session != null;
    public event Action<string>? Status;
    public event Action<TranscriptSegment>? Segment;
    public event Action<AudioFrame>? Audio;
    public async Task StartAsync(string mic, string? output, bool infer)
    {
        if (Active) return; var key = services.Settings.Get("Deepgram"); if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Save a Deepgram key in Settings first.");
        session = new(); Transcript.Clear();
        foreach (var role in infer ? new[] { "MIXED" } : new[] { "ME", "THEM" }) { var stt = new DeepgramStream(key, role, services.Settings.Value, services.Log); stt.Segment += s => { var canonical = Transcript.Update(role, s); Segment?.Invoke(canonical); }; stt.Status += s => Status?.Invoke(s); streams[role] = stt; running.Add(stt.RunAsync(session.Token)); }
        audio.Frame += OnFrame; audio.Failure += OnFailure;
        try { audio.Start(mic, output, infer); Status?.Invoke(infer ? "Infer: map diarized speakers manually" : "Clear: mic + endpoint loopback active"); } catch { await StopAsync(); throw; }
    }
    private void OnFrame(AudioFrame frame) { streams.GetValueOrDefault(frame.Stream)?.TryWrite(frame.Pcm); Audio?.Invoke(frame); }
    private void OnFailure(string s) { services.Log.Event("audio", "failure"); Status?.Invoke(s); }
    public async Task FeedWavAsync(string path, CancellationToken ct)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path); NAudio.Wave.ISampleProvider samples = reader;
        if (reader.WaveFormat.Channels == 2) samples = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(samples);
        if (samples.WaveFormat.Channels != 1) throw new InvalidOperationException("WAV harness supports mono or stereo files.");
        var resample = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(samples, 24000); var block = new float[2400]; int n;
        while ((n = resample.Read(block, 0, block.Length)) > 0) { ct.ThrowIfCancellationRequested(); byte[] pcm = new byte[n * 2]; double energy = 0; for (int i = 0; i < n; i++) { var f = Math.Clamp(block[i], -1, 1); short s = (short)(f * 32767); pcm[i * 2] = (byte)s; pcm[i * 2 + 1] = (byte)(s >> 8); energy += f * f; } OnFrame(new("THEM", pcm, DateTimeOffset.UtcNow, Math.Sqrt(energy / n))); await Task.Delay(TimeSpan.FromSeconds(n / 24000.0), ct); }
    }
    public async Task StartWavAsync()
    {
        if (Active) throw new InvalidOperationException("Stop the session before feeding a WAV."); var key = services.Settings.Get("Deepgram") ?? throw new InvalidOperationException("Save a Deepgram key first."); session = new(); Transcript.Clear(); var stt = new DeepgramStream(key, "THEM", services.Settings.Value, services.Log); stt.Segment += s => { var canonical = Transcript.Update("THEM", s); Segment?.Invoke(canonical); }; stt.Status += s => Status?.Invoke(s); streams["THEM"] = stt; running.Add(stt.RunAsync(session.Token)); await Task.CompletedTask;
    }
    public async Task StopAsync() { if (session == null) return; audio.Frame -= OnFrame; audio.Failure -= OnFailure; audio.Stop(); foreach (var stream in streams.Values) stream.Complete(); try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { } session.Cancel(); foreach (var stream in streams.Values) await stream.DisposeAsync(); try { await Task.WhenAll(running); } catch (OperationCanceledException) { } streams.Clear(); running.Clear(); session.Dispose(); session = null; Status?.Invoke("Stopped"); }
    public async ValueTask DisposeAsync() { await StopAsync(); audio.Dispose(); }
}
