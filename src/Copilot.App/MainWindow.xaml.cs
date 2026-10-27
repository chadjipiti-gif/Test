using System.Windows;
namespace Copilot.App;
public partial class MainWindow : Window
{
 public MainWindow() { InitializeComponent(); }
 private void Settings_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Settings");
}
