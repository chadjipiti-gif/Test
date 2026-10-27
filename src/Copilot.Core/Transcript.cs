namespace Copilot.Core;
public enum SpeakerRole { Me, Them, Unknown }
public record TranscriptSegment(Guid Id,SpeakerRole Role,string Text,bool IsFinal,TimeSpan Start,TimeSpan End,double? Confidence=null,string? SpeakerId=null)
{
 public override string ToString()=>$"{(Role==SpeakerRole.Me?"ME":Role==SpeakerRole.Them?"THEM":"UNKNOWN")} {(IsFinal?"":"[partial] ")}{Text}";
}
public record TranscriptSnapshot(long Version,IReadOnlyList<TranscriptSegment> Segments);
public sealed class TranscriptState
{
 private readonly object gate=new();private readonly List<TranscriptSegment> history=[];private readonly Dictionary<string,TranscriptSegment> partials=[];private long version;
 public void Update(string stream,TranscriptSegment segment){lock(gate){if(segment.IsFinal){partials.Remove(stream);if(!history.Any(s=>s.Id==segment.Id)){history.Add(segment);version++;}}else partials[stream]=segment;}}
 public void Correct(Guid id,SpeakerRole role){lock(gate){var i=history.FindIndex(x=>x.Id==id);if(i>=0){history[i]=history[i] with{Role=role};version++;}}}
 public void MapSpeaker(string speaker,SpeakerRole role){lock(gate){for(int i=0;i<history.Count;i++)if(history[i].SpeakerId==speaker)history[i]=history[i] with{Role=role};version++;}}
 public TranscriptSnapshot Freeze(bool includePartial=false){lock(gate)return new(version,history.Concat(includePartial?partials.Values:[]).OrderBy(x=>x.Start).ToArray());}
 public IReadOnlyList<TranscriptSegment> Recent()=>Freeze(true).Segments.TakeLast(60).ToArray();
 public void Clear(){lock(gate){history.Clear();partials.Clear();version++;}}
}
public interface ITranscriptionStream : IAsyncDisposable
{
 event Action<TranscriptSegment>? Segment;
 event Action<string>? Status;
 Task RunAsync(CancellationToken ct);
 bool TryWrite(byte[] pcm);
}
