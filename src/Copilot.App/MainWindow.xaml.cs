using System.Windows;
using System.Windows.Controls;
namespace Copilot.App;
public partial class MainWindow : Window
{
 private readonly Services services=new();
 public MainWindow() { InitializeComponent(); }
 private void Settings_Click(object s, RoutedEventArgs e) => new SettingsWindow(services) { Owner=this }.ShowDialog();
 private void Copy_Click(object s, RoutedEventArgs e) { var c=new[]{CardA,CardB,CardC}[int.Parse((string)((Button)s).Tag)]; if(!string.IsNullOrWhiteSpace(c.Text)) Clipboard.SetText(c.Text); }
 private void Dismiss_Click(object s, RoutedEventArgs e) => new[]{CardA,CardB,CardC}[int.Parse((string)((Button)s).Tag)].Text="";
 private void Start_Click(object s, RoutedEventArgs e) { }
 private void Help_Click(object s, RoutedEventArgs e) { }
 private void Wav_Click(object s, RoutedEventArgs e) { }
 private void Correct_Click(object s, RoutedEventArgs e) { }
}
