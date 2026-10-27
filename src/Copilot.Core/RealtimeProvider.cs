using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
namespace Copilot.Core;
// GA Realtime schema, text-only responses, manual audio commits. App state is re-injected every turn.
public sealed class RealtimeProvider(IKeyStore keys,Diagnostics log) : ISuggestionProvider,IAsyncDisposable
{
 private readonly SemaphoreSlim send=new(1,1),generate=new(1,1);
 private ClientWebSocket? socket;private CancellationTokenSource? life;private Task? receiver,sender;
 private Channel<byte[]>? audio;private TaskCompletionSource<string>? committed,response;
 private int pendingBytes;private readonly List<string> audioItems=[];
 public event Action<string>? Status;
 public bool TryAudio(byte[] pcm)=>audio?.Writer.TryWrite(pcm)??false;
 public async Task ConnectAsync(string model,CancellationToken ct)
 {
  await DisconnectAsync();var key=keys.Get("OpenAI")??throw new InvalidOperationException("OpenAI key missing");
  socket=new();socket.Options.SetRequestHeader("Authorization","Bearer "+key);life=new();
  try
  {
   await socket.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?model="+Uri.EscapeDataString(model)),ct);
   await WebSocketJson.SendAsync(socket,new{type="session.update",session=new{type="realtime",model,output_modalities=new[]{"text"},audio=new{input=new{format=new{type="audio/pcm",rate=24000},turn_detection=(object?)null}},instructions="Privately suggest short speakable responses for ME. Do not respond until requested."}},ct);
   audio=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(100){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
   receiver=ReceiveAsync(socket,life.Token);sender=SendAudioAsync(socket,audio.Reader,life.Token);Status?.Invoke("Realtime connected");
  }
  catch{await DisconnectAsync();throw;}
 }
 private async Task SendAudioAsync(ClientWebSocket ws,ChannelReader<byte[]> reader,CancellationToken ct)
 {
  try{await foreach(var bytes in reader.ReadAllAsync(ct)){await send.WaitAsync(ct);try{
   // Keep audio buffer bounded even when no turns are committed (e.g. long silence).
   if(pendingBytes>24000*2*25){await WebSocketJson.SendAsync(ws,new{type="input_audio_buffer.clear"},ct);pendingBytes=0;}
   await WebSocketJson.SendAsync(ws,new{type="input_audio_buffer.append",audio=Convert.ToBase64String(bytes)},ct);pendingBytes+=bytes.Length;
  }finally{send.Release();}}}catch(OperationCanceledException)when(ct.IsCancellationRequested){}catch(Exception ex){response?.TrySetException(ex);Status?.Invoke("Realtime audio disconnected; A/B continue");}
 }
 private async Task ReceiveAsync(ClientWebSocket ws,CancellationToken ct)
 {
  try{while(!ct.IsCancellationRequested){using var doc=await WebSocketJson.ReceiveAsync(ws,ct);var root=doc.RootElement;var type=root.GetProperty("type").GetString();
   if(type=="input_audio_buffer.committed"){var id=root.GetProperty("item_id").GetString()!;committed?.TrySetResult(id);}
   else if(type=="response.done")
   {
    var r=root.GetProperty("response");if(r.GetProperty("status").GetString()!="completed"){response?.TrySetException(new InvalidOperationException("Realtime response incomplete"));continue;}
    var texts=new List<string>();foreach(var item in r.GetProperty("output").EnumerateArray()){if(item.TryGetProperty("content",out var content))foreach(var c in content.EnumerateArray())if(c.TryGetProperty("text",out var t))texts.Add(t.GetString()!);}
    if(texts.Count==0)response?.TrySetException(new InvalidOperationException("No text returned"));else response?.TrySetResult(string.Join("\n",texts));
   }
   else if(type=="error"){log.Event("realtime","provider_error");var ex=new InvalidOperationException("Realtime rejected request");committed?.TrySetException(ex);response?.TrySetException(ex);Status?.Invoke("Realtime error — check key/model access");}
  }}catch(OperationCanceledException)when(ct.IsCancellationRequested){}catch(Exception ex){committed?.TrySetException(ex);response?.TrySetException(ex);Status?.Invoke("Realtime disconnected; restart session to retry");}
 }
 public async Task<string> GenerateAsync(string prompt,BoxConfig config,ModelEntry model,CancellationToken ct)
 {
  await generate.WaitAsync(ct);
  try
  {
   if(socket?.State!=WebSocketState.Open)throw new InvalidOperationException("Realtime disconnected; restart session");
   var ws=socket;var input=new List<object>();response=new(TaskCreationOptions.RunContinuationsAsynchronously);
   await send.WaitAsync(ct);bool hasAudio;
   try{hasAudio=pendingBytes>=4800;if(hasAudio){committed=new(TaskCreationOptions.RunContinuationsAsynchronously);await WebSocketJson.SendAsync(ws,new{type="input_audio_buffer.commit"},ct);pendingBytes=0;}}finally{send.Release();}
   if(hasAudio){var id=await committed!.Task.WaitAsync(ct);audioItems.Add(id);}
   foreach(var id in audioItems.TakeLast(2))input.Add(new{type="item_reference",id});
   input.Add(new{type="message",role="user",content=new[]{new{type="input_text",text=prompt}}});
   await send.WaitAsync(ct);try{await WebSocketJson.SendAsync(ws,new{type="response.create",response=new{conversation="none",output_modalities=new[]{"text"},input,max_output_tokens=config.MaxTokens}},ct);}finally{send.Release();}
   var text=await response.Task.WaitAsync(ct);log.Content("realtime_response",text);
   // Retain at most two audio items server-side. No provider-owned history is authoritative.
   while(audioItems.Count>2){var old=audioItems[0];audioItems.RemoveAt(0);await send.WaitAsync(ct);try{await WebSocketJson.SendAsync(ws,new{type="conversation.item.delete",item_id=old},ct);}finally{send.Release();}}
   return text;
  }
  catch(OperationCanceledException){socket?.Abort();Status?.Invoke("Realtime request cancelled; restart session to reconnect");throw;}
  finally{response=null;committed=null;generate.Release();}
 }
 public async Task DisconnectAsync(){life?.Cancel();socket?.Abort();foreach(var task in new[]{receiver,sender})if(task!=null)try{await task;}catch(OperationCanceledException){}socket?.Dispose();life?.Dispose();socket=null;life=null;audio=null;receiver=sender=null;pendingBytes=0;audioItems.Clear();}
 public async ValueTask DisposeAsync()=>await DisconnectAsync();
}
