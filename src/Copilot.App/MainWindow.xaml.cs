using System.Windows;
using Copilot.Core;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
namespace Copilot.App;
public partial class MainWindow : Window
{
 private readonly Services services;
 private readonly SessionController session;
 private readonly IAudioCaptureService audio;
 private CancellationTokenSource? wav;
 private readonly ContextStore context=new();
 private readonly MeetingStateService meeting=new();
 private readonly ITurnDetector detector;
 private readonly SuggestionOrchestrator orchestrator;
 private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
 private readonly DispatcherTimer autosave=new(){Interval=TimeSpan.FromMilliseconds(600)};
 public MainWindow(Services services,SessionController session,IAudioCaptureService audio) { this.services=services;this.session=session;this.audio=audio;InitializeComponent();
 detector=new TurnDetector(new SemanticGate(),services.Log);
 orchestrator=new(services.Registry,new PromptAssembler(new KeywordRetriever(),services.Log),new OpenRouterProvider(services.Http,services.Settings,services.Log),services.Log);
 var saved=services.Settings.Value;Notes.Text=saved.Notes;WhoMe.Text=saved.Summary.WhoMe;WhoThem.Text=saved.Summary.WhoThem;Discussion.Text=saved.Summary.Discussion;Goal.Text=saved.Summary.Goal;context.Update(Notes.Text,false);
 foreach(var box in new[]{Notes,WhoMe,WhoThem,Discussion,Goal})box.TextChanged+=(_,_)=>{context.Update(Notes.Text,session.Active);autosave.Stop();autosave.Start();};
 autosave.Tick+=(_,_)=>{autosave.Stop();saved.Notes=Notes.Text;saved.Summary=Summary();try{services.Settings.Persist();}catch{Status.Text="Local autosave failed";}};
 session.Audio+=frame=>{if(frame.Stream is "THEM" or "MIXED")detector.Audio(frame.Energy,frame.CapturedAt);};
 session.Segment+=seg=>{detector.Transcript(seg,DateTimeOffset.UtcNow);if(seg.IsFinal)_=meeting.UpdateAsync(session.Transcript.Freeze(),services.Settings.Value.Summary);};
 timer.Tick+=(_,_)=>{if(session.Active&&detector.ShouldCommit(DateTimeOffset.UtcNow,saved.PauseMs,saved.FallbackMs))Commit();};timer.Start();
 services.ModelChanged+=i=>{orchestrator.ChangeConfiguration(i);Dispatcher.InvokeAsync(()=>BoxStatus.Text="Model configuration changed");};
 orchestrator.Result+=result=>Dispatcher.InvokeAsync(()=>{
 var i=result.Id.Box[0]-'A';var card=new[]{CardA,CardB,CardC}[i];
 if(result.State=="ready"){card.Text=result.Text;card.ToolTip=result.Id.ToString();Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{var ms=(DateTimeOffset.UtcNow-result.LastAudible).TotalMilliseconds;services.Log.Latency(result.Id.Box,Math.Max(0,ms));services.Log.Event("ui","paint_scheduled",new{result.Id});}));}
 else if(result.State=="disabled")card.Text="Disabled";
 BoxStatus.Text=$"{result.Id.Box}: {result.State}";
 });
 Loaded+=(_,_)=>RefreshDevices();
 session.Status+=s=>Dispatcher.InvokeAsync(()=>Status.Text=s);
 session.Segment+=s=>Dispatcher.InvokeAsync(RefreshTranscript);
 Closing+=async (_,e)=>{if(session.Active){e.Cancel=true;wav?.Cancel();await session.StopAsync();Close();}};
 }
 private void RefreshDevices(){try{Microphone.ItemsSource=audio.Devices(true);Output.ItemsSource=audio.Devices(false);Microphone.SelectedIndex=0;Output.SelectedIndex=0;}catch(Exception ex){Status.Text="Device enumeration failed: "+ex.GetType().Name;}}
 private void RefreshTranscript(){Transcript.ItemsSource=session.Transcript.Recent();if(Transcript.Items.Count>0)Transcript.ScrollIntoView(Transcript.Items[^1]);}
 private void Settings_Click(object s, RoutedEventArgs e) => new SettingsWindow(services) { Owner=this }.ShowDialog();
 private void Copy_Click(object s, RoutedEventArgs e) { var c=new[]{CardA,CardB,CardC}[int.Parse((string)((Button)s).Tag)]; if(!string.IsNullOrWhiteSpace(c.Text)) Clipboard.SetText(c.Text); }
 private void Dismiss_Click(object s, RoutedEventArgs e) => new[]{CardA,CardB,CardC}[int.Parse((string)((Button)s).Tag)].Text="";
 private async void Start_Click(object s, RoutedEventArgs e) { Start.IsEnabled=false;try{if(session.Active){wav?.Cancel();orchestrator.CancelAll();await session.StopAsync();Start.Content="Start session";}else{if(Microphone.SelectedItem is not AudioDevice mic)throw new InvalidOperationException("Select a microphone.");meeting.Reset();await session.StartAsync(mic.Id,(Output.SelectedItem as AudioDevice)?.Id,Mode.SelectedIndex==1);Start.Content="Stop session";}}catch(Exception ex){Status.Text=ex is InvalidOperationException?ex.Message:"Session error: "+ex.GetType().Name;}finally{Start.IsEnabled=true;} }
 private StaticSummary Summary()=>new(WhoMe.Text,WhoThem.Text,Discussion.Text,Goal.Text);
 private void Commit(){if(!session.Active){Status.Text="Start a session first.";return;}var now=DateTimeOffset.UtcNow;detector.Committed(now);var snapshot=session.Transcript.Freeze(true);var cfg=services.Settings.Value.Boxes.Select(c=>System.Text.Json.JsonSerializer.Deserialize<BoxConfig>(System.Text.Json.JsonSerializer.Serialize(c))!).ToArray();var limit=new[]{1200,3000,6000,16000}[Math.Max(0,Depth.SelectedIndex)];foreach(var c in cfg)c.TranscriptBudget=Math.Min(c.TranscriptBudget,limit);var turn=new TurnCommit(Guid.NewGuid(),snapshot,context.Freeze(),Summary(),meeting.Current,detector.LastAudible==default?now:detector.LastAudible,now,string.Join(" ",snapshot.Segments.Where(x=>x.Role==SpeakerRole.Them).TakeLast(3).Select(x=>x.Text)));services.Log.Event("turn","commit",new{turn.TurnId,snapshot.Version});_=orchestrator.CommitAsync(turn,cfg);}
 private void Help_Click(object s, RoutedEventArgs e) => Commit();
 private async void Wav_Click(object s, RoutedEventArgs e) {var dialog=new Microsoft.Win32.OpenFileDialog{Filter="WAV audio|*.wav"};if(dialog.ShowDialog()!=true)return;try{wav=new();await session.StartWavAsync();Start.Content="Stop session";await session.FeedWavAsync(dialog.FileName,wav.Token);Status.Text="WAV complete; waiting for transcription. Stop session when finished.";}catch(OperationCanceledException){}catch(Exception ex){Status.Text="WAV failed: "+ex.GetType().Name;await session.StopAsync();}}
 private void Correct_Click(object s, RoutedEventArgs e) {if(Transcript.SelectedItem is TranscriptSegment seg && seg.IsFinal){var role=seg.Role==SpeakerRole.Me?SpeakerRole.Them:SpeakerRole.Me;if(seg.SpeakerId!=null)session.Transcript.MapSpeaker(seg.SpeakerId,role);else session.Transcript.Correct(seg.Id,role);RefreshTranscript();}}
}
