using System.Windows;
using Microsoft.Extensions.DependencyInjection;
namespace Copilot.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); var services = new ServiceCollection().AddSingleton<Services>().AddSingleton<IAudioCaptureService, AudioCaptureService>().AddSingleton<SessionController>().AddSingleton<MainWindow>().BuildServiceProvider(); services.GetRequiredService<MainWindow>().Show(); }
}
