using System.Windows;
using Copilot.Core;
using System.Windows.Controls;
namespace Copilot.App;
public partial class MainWindow : Window
{
 private readonly Services services;
 private readonly SessionController session;
 private readonly IAudioCaptureService audio;
 private CancellationTokenSource? wav;
 public MainWindow(Services services,SessionController session,IAudioCaptureService audio) { this.services=services;this.session=session;this.audio=audio;InitializeComponent();
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
 private async void Start_Click(object s, RoutedEventArgs e) { Start.IsEnabled=false;try{if(session.Active){wav?.Cancel();await session.StopAsync();Start.Content="Start session";}else{if(Microphone.SelectedItem is not AudioDevice mic)throw new InvalidOperationException("Select a microphone.");await session.StartAsync(mic.Id,(Output.SelectedItem as AudioDevice)?.Id,Mode.SelectedIndex==1);Start.Content="Stop session";}}catch(Exception ex){Status.Text=ex is InvalidOperationException?ex.Message:"Session error: "+ex.GetType().Name;}finally{Start.IsEnabled=true;} }
 private void Help_Click(object s, RoutedEventArgs e) { }
 private async void Wav_Click(object s, RoutedEventArgs e) {var dialog=new Microsoft.Win32.OpenFileDialog{Filter="WAV audio|*.wav"};if(dialog.ShowDialog()!=true)return;try{wav=new();await session.StartWavAsync();Start.Content="Stop session";await session.FeedWavAsync(dialog.FileName,wav.Token);Status.Text="WAV complete; waiting for transcription. Stop session when finished.";}catch(OperationCanceledException){}catch(Exception ex){Status.Text="WAV failed: "+ex.GetType().Name;await session.StopAsync();}}
 private void Correct_Click(object s, RoutedEventArgs e) {if(Transcript.SelectedItem is TranscriptSegment seg && seg.IsFinal){var role=seg.Role==SpeakerRole.Me?SpeakerRole.Them:SpeakerRole.Me;if(seg.SpeakerId!=null)session.Transcript.MapSpeaker(seg.SpeakerId,role);else session.Transcript.Correct(seg.Id,role);RefreshTranscript();}}
}
