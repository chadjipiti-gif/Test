using System.Text.Json;
namespace Copilot.Core;
public sealed class Diagnostics(string directory)
{
 private readonly object gate=new();
 private readonly Dictionary<string,List<double>> samples=[];
 public bool ContentEnabled { get; set; }
 public void Event(string module,string state,object? metadata=null)
 { lock(gate){Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,$"operations-{DateTime.UtcNow:yyyyMMdd}.jsonl"),JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,module,state,metadata})+Environment.NewLine);} }
 // Only call with application content, never credentials, URLs with keys, or exception messages.
 public void Content(string stage,string text){if(ContentEnabled)Event("content",stage,new{text});}
 public void Latency(string box,double ms){lock(gate){if(!samples.TryGetValue(box,out var s))samples[box]=s=[];s.Add(ms);if(s.Count>2000)s.RemoveAt(0);}Event("latency",box,new{milliseconds=ms});}
 public (double P50,double P95,double P99) Percentiles(string box){lock(gate){if(!samples.TryGetValue(box,out var s)||s.Count==0)return(0,0,0);var a=s.Order().ToArray();double P(double q)=>a[Math.Clamp((int)Math.Ceiling(q*a.Length)-1,0,a.Length-1)];return(P(.5),P(.95),P(.99));}}
}
