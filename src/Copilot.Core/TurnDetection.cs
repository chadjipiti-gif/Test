namespace Copilot.Core;
public interface ISemanticGate { bool Complete(string text); }
public sealed class SemanticGate : ISemanticGate
{
 public bool Complete(string text){var s=text.Trim();return s.Length>4 && !new[]{" and"," but"," because"," if"," or"," to"," so"," the"," a"}.Any(x=>s.TrimEnd('.', '?', '!').EndsWith(x,StringComparison.OrdinalIgnoreCase)) && (s.EndsWith('?')||s.EndsWith('.')||s.EndsWith('!'));}
}
public interface ITurnDetector
{
 void Audio(double energy,DateTimeOffset now);
 void Transcript(TranscriptSegment segment,DateTimeOffset now);
 bool ShouldCommit(DateTimeOffset now,int pauseMs,int fallbackMs);
 DateTimeOffset LastAudible { get; }
 void Committed(DateTimeOffset now);
}
public sealed class TurnDetector(ISemanticGate semantic,Diagnostics log) : ITurnDetector
{
 private readonly object gate=new();private string text="";private DateTimeOffset lastText,lastCommit;private bool pending;private DateTimeOffset audible;
 public DateTimeOffset LastAudible { get {lock(gate)return audible;} }
 public void Audio(double energy,DateTimeOffset now){if(energy<.012)return;lock(gate){if(!pending&&lastCommit!=default&&(now-lastCommit).TotalMilliseconds<1200){log.Event("turn","probable_false_endpoint");lastCommit=default;}audible=now;}}
 public void Transcript(TranscriptSegment segment,DateTimeOffset now){if(segment.Role!=SpeakerRole.Them)return;lock(gate){pending=true;text=segment.Text;lastText=now;if(audible==default)audible=now;}}
 public bool ShouldCommit(DateTimeOffset now,int pauseMs,int fallbackMs){lock(gate)return pending&&(now-audible).TotalMilliseconds>=pauseMs&&((semantic.Complete(text)&&(now-lastText).TotalMilliseconds>=350)||(now-lastText).TotalMilliseconds>=fallbackMs);}
 public void Committed(DateTimeOffset now){lock(gate){pending=false;lastCommit=now;}}
}
