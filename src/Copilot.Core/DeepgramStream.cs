using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
namespace Copilot.Core;
public sealed class DeepgramStream(string key, string stream, UserSettings settings, Diagnostics log) : ITranscriptionStream
{
    private readonly Channel<byte[]> audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(120) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private volatile bool closing;
    public void Complete() { closing = true; audio.Writer.TryComplete(); }
    public event Action<TranscriptSegment>? Segment;
    public event Action<string>? Status;
    public bool TryWrite(byte[] pcm) { var ok = audio.Writer.TryWrite(pcm); if (!ok) Status?.Invoke(stream + ": STT overloaded — audio dropped"); return ok; }
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token); ct = linked.Token; int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket(); ws.Options.SetRequestHeader("Authorization", "Token " + key);
            try
            {
                var query = $"model=nova-3&encoding=linear16&sample_rate=24000&channels=1&interim_results={settings.Interim.ToString().ToLowerInvariant()}&smart_format={settings.SmartFormat.ToString().ToLowerInvariant()}&diarize={(stream == "MIXED" && settings.Diarize).ToString().ToLowerInvariant()}&endpointing={settings.EndpointMs}&mip_opt_out=true";
                int termBudget = 0; foreach (var term in settings.Keyterms.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(30)) { termBudget += System.Text.Encoding.UTF8.GetByteCount(term); if (termBudget > 500) break; query += "&keyterm=" + Uri.EscapeDataString(term.Trim()); }
                var offset = DateTimeOffset.UtcNow; await ws.ConnectAsync(new Uri("wss://api.deepgram.com/v1/listen?" + query), ct); Status?.Invoke(stream + ": transcription connected");
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct); var sender = SendAsync(ws, connection.Token); var receiver = ReceiveAsync(ws, offset, connection.Token);
                await Task.WhenAny(sender, receiver); connection.Cancel(); try { await Task.WhenAll(sender, receiver); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new WebSocketException("Connection ended"); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { if (closing) break; log.Event("stt", "disconnected", new { stream, error = ex.GetType().Name }); Status?.Invoke(stream + ": transcription failure — reconnecting"); if (++attempt >= 5) { Status?.Invoke(stream + ": transcription failed; stop/start to retry"); break; } try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt * 2, 10)), ct); } catch (OperationCanceledException) { break; } while (audio.Reader.TryRead(out _)) { } }
        }
    }
    private async Task SendAsync(ClientWebSocket ws, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (audio.Reader.TryRead(out var b)) await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Binary, true, ct);
            else { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(2000); try { if (!await audio.Reader.WaitToReadAsync(timeout.Token)) { await WebSocketJson.SendAsync(ws, new { type = "CloseStream" }, ct); await Task.Delay(700, ct); return; } } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { await WebSocketJson.SendAsync(ws, new { type = "KeepAlive" }, ct); } }
        }
    }
    private async Task ReceiveAsync(ClientWebSocket ws, DateTimeOffset offset, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var doc = await WebSocketJson.ReceiveAsync(ws, ct); var r = doc.RootElement;
            if (r.TryGetProperty("type", out var type) && type.GetString() == "Error") throw new InvalidOperationException("Provider rejected transcription");
            foreach (var segment in DeepgramParser.Parse(r, stream, offset)) { Segment?.Invoke(segment); log.Event("stt", segment.IsFinal ? "final" : "partial", new { stream }); log.Content("transcript", segment.Text); }
        }
    }
    public ValueTask DisposeAsync() { lifetime.Cancel(); lifetime.Dispose(); return ValueTask.CompletedTask; }
}
