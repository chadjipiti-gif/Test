using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace Copilot.App;
public record AudioDevice(string Id,string Name);
public record AudioFrame(string Stream,byte[] Pcm,DateTimeOffset CapturedAt,double Energy);
public interface IAudioCaptureService : IDisposable
{
 IReadOnlyList<AudioDevice> Devices(bool microphone);
 event Action<AudioFrame>? Frame;
 event Action<string>? Failure;
 void Start(string microphoneId,string? outputId,bool infer);
 void Stop();
}
public sealed class AudioCaptureService : IAudioCaptureService
{
 private readonly List<(WasapiCapture Capture,MMDevice Device)> active=[];
 public event Action<AudioFrame>? Frame;
 public event Action<string>? Failure;
 public IReadOnlyList<AudioDevice> Devices(bool microphone){using var e=new MMDeviceEnumerator();return e.EnumerateAudioEndPoints(microphone?DataFlow.Capture:DataFlow.Render,DeviceState.Active).Select(x=>{var d=new AudioDevice(x.ID,x.FriendlyName);x.Dispose();return d;}).ToArray();}
 public void Start(string microphoneId,string? outputId,bool infer)
 {
  Stop();try { Add(microphoneId,infer?"MIXED":"ME",false);if(!infer){if(outputId==null)throw new InvalidOperationException("Meeting output required");Add(outputId,"THEM",true);} }catch{Stop();throw;}
 }
 private void Add(string id,string role,bool loopback)
 {
  using var enumerator=new MMDeviceEnumerator();var device=enumerator.GetDevice(id);WasapiCapture capture=loopback?new WasapiLoopbackCapture(device):new WasapiCapture(device);
  // A bounded memory buffer and stateful resampler preserve fractional samples across capture packets.
  var source=new BufferedWaveProvider(capture.WaveFormat){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=false,DiscardOnBufferOverflow=false};
  ISampleProvider samples=source.ToSampleProvider();
  if(samples.WaveFormat.Channels==2)samples=new StereoToMonoSampleProvider(samples);
  else if(samples.WaveFormat.Channels!=1) { var mono=new MultiplexingSampleProvider([samples],1);for(int c=0;c<samples.WaveFormat.Channels;c++)mono.ConnectInputToOutput(c,0);samples=mono; }
  var resampler=new WdlResamplingSampleProvider(samples,24000);var floats=new float[24000];
  capture.DataAvailable+=(_,e)=>{try { source.AddSamples(e.Buffer,0,e.BytesRecorded); int count;while((count=resampler.Read(floats,0,floats.Length))>0){var pcm=new byte[count*2];double energy=0;for(int i=0;i<count;i++){var f=Math.Clamp(floats[i],-1,1);short n=(short)(f*32767);pcm[i*2]=(byte)n;pcm[i*2+1]=(byte)(n>>8);energy+=f*f;}Frame?.Invoke(new(role,pcm,DateTimeOffset.UtcNow,Math.Sqrt(energy/count)));} }catch(Exception ex){Failure?.Invoke(role+" audio failure: "+ex.GetType().Name);} };
  capture.RecordingStopped+=(_,e)=>{if(e.Exception!=null)Failure?.Invoke(role+" device lost: "+e.Exception.GetType().Name);};
  active.Add((capture,device));capture.StartRecording();
 }
 public void Stop(){foreach(var (c,d) in active){c.StopRecording();c.Dispose();d.Dispose();}active.Clear();}
 public void Dispose()=>Stop();
}
