using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
namespace Copilot.Core;
public sealed class DeepgramStream(string key,string stream,UserSettings settings,Diagnostics log) : ITranscriptionStream
{
 private readonly Channel<byte[]> audio=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(120){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
 private readonly CancellationTokenSource lifetime=new();
 public event Action<TranscriptSegment>? Segment;
 public event Action<string>? Status;
 public bool TryWrite(byte[] pcm){var ok=audio.Writer.TryWrite(pcm);if(!ok)Status?.Invoke(stream+": STT overloaded — audio dropped");return ok;}
 public async Task RunAsync(CancellationToken ct)
 {
  using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);ct=linked.Token;int attempt=0;
  while(!ct.IsCancellationRequested)
  {
   using var ws=new ClientWebSocket();ws.Options.SetRequestHeader("Authorization","Token "+key);
   try
   {
    var query=$"model=nova-3&encoding=linear16&sample_rate=24000&channels=1&interim_results={settings.Interim.ToString().ToLowerInvariant()}&smart_format={settings.SmartFormat.ToString().ToLowerInvariant()}&diarize={(stream=="MIXED"&&settings.Diarize).ToString().ToLowerInvariant()}&endpointing={settings.EndpointMs}&mip_opt_out=true";
    foreach(var term in settings.Keyterms.Split('\n',StringSplitOptions.RemoveEmptyEntries).Take(30))query+="&keyterm="+Uri.EscapeDataString(term.Trim());
    var offset=DateTimeOffset.UtcNow;await ws.ConnectAsync(new Uri("wss://api.deepgram.com/v1/listen?"+query),ct);Status?.Invoke(stream+": transcription connected");
    using var connection=CancellationTokenSource.CreateLinkedTokenSource(ct);var sender=SendAsync(ws,connection.Token);var receiver=ReceiveAsync(ws,offset,connection.Token);
    await Task.WhenAny(sender,receiver);connection.Cancel();try{await Task.WhenAll(sender,receiver);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new WebSocketException("Connection ended");}
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
   catch(Exception ex){log.Event("stt","disconnected",new{stream,error=ex.GetType().Name});Status?.Invoke(stream+": transcription failure — reconnecting");if(++attempt>=5){Status?.Invoke(stream+": transcription failed; stop/start to retry");break;}try{await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt*2,10)),ct);}catch(OperationCanceledException){break;}while(audio.Reader.TryRead(out _)){} }
  }
 }
 private async Task SendAsync(ClientWebSocket ws,CancellationToken ct)
 {
  while(!ct.IsCancellationRequested)
  {
   if(audio.Reader.TryRead(out var b))await ws.SendAsync(new ArraySegment<byte>(b),WebSocketMessageType.Binary,true,ct);
   else {using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(2000);try{await audio.Reader.WaitToReadAsync(timeout.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){await WebSocketJson.SendAsync(ws,new{type="KeepAlive"},ct);}}
  }
 }
 private async Task ReceiveAsync(ClientWebSocket ws,DateTimeOffset offset,CancellationToken ct)
 {
  while(!ct.IsCancellationRequested){using var doc=await WebSocketJson.ReceiveAsync(ws,ct);var r=doc.RootElement;
   if(r.TryGetProperty("type",out var type)&&type.GetString()=="Error")throw new InvalidOperationException("Provider rejected transcription");
   if(!r.TryGetProperty("channel",out var channel))continue;var alt=channel.GetProperty("alternatives")[0];var text=alt.GetProperty("transcript").GetString();if(string.IsNullOrWhiteSpace(text))continue;
   bool final=r.GetProperty("is_final").GetBoolean();double start=r.GetProperty("start").GetDouble(),duration=r.GetProperty("duration").GetDouble();double? confidence=alt.TryGetProperty("confidence",out var c)?c.GetDouble():null;
   var role=stream=="ME"?SpeakerRole.Me:stream=="THEM"?SpeakerRole.Them:SpeakerRole.Unknown;
   var baseTime=TimeSpan.FromTicks(offset.UtcTicks);
   if(stream=="MIXED"&&final&&alt.TryGetProperty("words",out var words))
   {
    var group=new List<JsonElement>();string? last=null;
    void Emit(){if(group.Count==0)return;var first=group[0];var end=group[^1];var t=string.Join(" ",group.Select(w=>w.TryGetProperty("punctuated_word",out var p)?p.GetString():w.GetProperty("word").GetString()));Segment?.Invoke(new(Guid.NewGuid(),SpeakerRole.Unknown,t,true,baseTime+TimeSpan.FromSeconds(first.GetProperty("start").GetDouble()),baseTime+TimeSpan.FromSeconds(end.GetProperty("end").GetDouble()),confidence,last));group.Clear();}
    foreach(var w in words.EnumerateArray()){var speaker=w.TryGetProperty("speaker",out var sp)?sp.ToString():null;if(speaker!=last)Emit();last=speaker;group.Add(w);}Emit();
   }
   else Segment?.Invoke(new(Guid.NewGuid(),role,text,final,baseTime+TimeSpan.FromSeconds(start),baseTime+TimeSpan.FromSeconds(start+duration),confidence));
   log.Event("stt",final?"final":"partial",new{stream});log.Content("transcript",text);
  }
 }
 public ValueTask DisposeAsync(){lifetime.Cancel();lifetime.Dispose();return ValueTask.CompletedTask;}
}
