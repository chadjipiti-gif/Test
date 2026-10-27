using System.Windows;
namespace Copilot.App;
public class SettingsWindow : Window
{
 public SettingsWindow() { Title="Settings"; Width=650; Height=750; Content=new System.Windows.Controls.TextBlock { Text="Provider connections and model settings", Margin=new Thickness(20) }; }
}
