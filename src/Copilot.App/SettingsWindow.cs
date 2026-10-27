using System.Windows;
using System.Windows.Controls;
using Copilot.Core;
namespace Copilot.App;
public class SettingsWindow : Window
{
 private readonly StackPanel panel=new(){Margin=new Thickness(16)};
 private readonly Services services;
 private readonly ComboBox[] models=new ComboBox[3];
 public SettingsWindow(Services services)
 {
  this.services=services; Title="Settings"; Width=680;Height=850; Content=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
  Add("Credentials — encrypted for this Windows user");
  foreach(var provider in new[]{"Deepgram","OpenRouter","OpenAI"})
  {
   Add(provider); var key=new PasswordBox { Margin=new Thickness(3) };panel.Children.Add(key);
   var state=Add("Not configured"); try{if(services.Settings.Get(provider)!=null)state.Text="Saved (not tested)";}catch{state.Text="Connection error: cannot decrypt saved key";}
   var row=new StackPanel { Orientation=Orientation.Horizontal };panel.Children.Add(row);
   Button(row,"Save",()=>{try{services.Settings.Save(provider,key.Password);key.Clear();state.Text="Saved (not tested)";}catch{state.Text="Save failed";}});
   Button(row,"Test Connection",async ()=>{state.Text="Testing…";try{var k=string.IsNullOrWhiteSpace(key.Password)?services.Settings.Get(provider):key.Password;using var c=new CancellationTokenSource(TimeSpan.FromSeconds(20));await services.Registry.TestAsync(provider,k??"",c.Token);state.Text="Connected";}catch(Exception ex){state.Text="Connection error: "+ex.GetType().Name;}});
  }
  Button(panel,"Refresh OpenRouter model catalog",async ()=>{try{await services.Registry.RefreshOpenRouterAsync(CancellationToken.None);Populate();}catch{MessageBox.Show(this,"Catalog unavailable. Check connectivity to openrouter.ai. Existing selections are preserved.");}});
  for(int i=0;i<3;i++) { int b=i;Add($"Suggestion {(char)('A'+i)}"); models[i]=new ComboBox();panel.Children.Add(models[i]); var cfg=services.Settings.Value.Boxes[i];
   Number("Transcript token budget",cfg.TranscriptBudget,v=>cfg.TranscriptBudget=(int)v,200,16000);
   Number("Retrieved context token budget",cfg.ContextBudget,v=>cfg.ContextBudget=(int)v,100,4000);
   Number("Max output tokens",cfg.MaxTokens,v=>cfg.MaxTokens=(int)v,32,1500);
   models[i].SelectionChanged+=(_,_)=>{ if(models[b].SelectedItem is ModelEntry m) { cfg.Model=string.IsNullOrEmpty(m.Id)?null:m.Id;services.ConfigurationChanged(b); } };
  }
  Add("Model sampling defaults: temperature 0.3, top_p 1. Unsupported parameters are omitted. Reasoning uses low effort when supported.");
  Add("Transcription: Deepgram Nova-3 (streaming)");
  Check("Interim results",services.Settings.Value.Interim,v=>services.Settings.Value.Interim=v);
  Check("Diarization (Infer)",services.Settings.Value.Diarize,v=>services.Settings.Value.Diarize=v);
  Check("Smart formatting",services.Settings.Value.SmartFormat,v=>services.Settings.Value.SmartFormat=v);
  Add("Keyterms (one per line)"); var terms=new TextBox{Text=services.Settings.Value.Keyterms,AcceptsReturn=true,Height=65}; terms.TextChanged+=(_,_)=>services.Settings.Value.Keyterms=terms.Text;panel.Children.Add(terms);
  Number("STT endpoint pause (ms)",services.Settings.Value.EndpointMs,v=>services.Settings.Value.EndpointMs=(int)v,100,2000);
  Number("Turn silence gate (ms)",services.Settings.Value.PauseMs,v=>services.Settings.Value.PauseMs=(int)v,400,3000);
  Number("Turn fallback timeout (ms)",services.Settings.Value.FallbackMs,v=>services.Settings.Value.FallbackMs=(int)v,2000,10000);
  Check("Debug Content Logging — includes private meeting content locally",services.Settings.Value.DebugContentLogging,v=>{services.Settings.Value.DebugContentLogging=v;services.Log.ContentEnabled=v;});
  Button(panel,"Open local diagnostics folder",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",LocalSettings.Root){UseShellExecute=true}));
  Populate();Closed+=(_,_)=>services.Settings.Persist();
 }
 private void Populate(){for(int i=0;i<3;i++){var id=services.Settings.Value.Boxes[i].Model;var list=new[]{ProviderRegistry.None}.Concat(services.Registry.Models.Where(m=>m.Capability==(i==2?Capability.Realtime:Capability.Text))).ToList();if(id!=null&&list.All(x=>x.Id!=id))list.Add(new(id,"Unavailable (refresh catalog): "+id,"OpenRouter",Capability.Text,false,[],false));models[i].ItemsSource=list;models[i].SelectedItem=list.FirstOrDefault(x=>x.Id==id)??ProviderRegistry.None;}}
 private TextBlock Add(string t){var b=new TextBlock{Text=t,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,10,3,3)};panel.Children.Add(b);return b;}
 private static void Button(Panel p,string t,Action action){var b=new Button{Content=t};b.Click+=(_,_)=>action();p.Children.Add(b);}
 private void Check(string t,bool value,Action<bool> setter){var c=new CheckBox{Content=t,IsChecked=value,Margin=new Thickness(3)};c.Click+=(_,_)=>setter(c.IsChecked==true);panel.Children.Add(c);}
 private void Number(string label,double value,Action<double> setter,double min,double max){Add(label);var t=new TextBox{Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture)};t.LostFocus+=(_,_)=>{if(double.TryParse(t.Text,System.Globalization.CultureInfo.InvariantCulture,out var n)&&n>=min&&n<=max)setter(n);else t.Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture);};panel.Children.Add(t);}
}
