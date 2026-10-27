using System.IO;
using System.Text;
using System.Windows;
using Copilot.App;
using Copilot.Core;
using Xunit;
namespace Copilot.Windows.Tests;
public class WindowsTests
{
    [Fact]
    public void SavedKeyIsEncryptedAndRoundTripsForCurrentUser()
    {
        var directory = Path.Combine(Path.GetTempPath(), "copilot-windows-tests", Guid.NewGuid().ToString()); var settings = new LocalSettings(directory); const string key = "unit-test-key-not-a-real-credential"; settings.Save("Deepgram", key); Assert.Equal(key, settings.Get("Deepgram")); Assert.DoesNotContain(key, Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "Deepgram.key")))); settings.Value.Notes = "local test note"; settings.Persist(); Assert.DoesNotContain(key, File.ReadAllText(Path.Combine(directory, "settings.json"))); Assert.Equal("local test note", new LocalSettings(directory).Value.Notes); Directory.Delete(directory, true);
    }
    [Fact]
    public void WpfShellAndSettingsConstructOnStaThread()
    {
        Exception? failure = null; var thread = new Thread(() => { try { var services = new Services(); using var audio = new FakeAudio(); var session = new SessionController(audio, services); var window = new MainWindow(services, session, audio); window.Measure(new Size(1000, 1000)); window.Arrange(new Rect(0, 0, 1000, 1000)); Assert.NotNull(window.FindName("Notes")); Assert.NotNull(window.FindName("CardA")); Assert.NotNull(window.FindName("Transcript")); var settings = new SettingsWindow(services); Assert.NotNull(settings.Content); settings.Close(); window.Close(); } catch (Exception ex) { failure = ex; } }); thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF smoke test timed out"); Assert.Null(failure);
    }
    [Fact] public void SurroundAudioIsDownmixedAcrossAllChannels(){var source=new Samples();var mono=new MonoDownmix(source);var output=new float[2];Assert.Equal(2,mono.Read(output,0,2));Assert.Equal(.5f,output[0]);Assert.Equal(.25f,output[1]);}
    private sealed class Samples:NAudio.Wave.ISampleProvider{public NAudio.Wave.WaveFormat WaveFormat=>NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000,4);public int Read(float[] buffer,int offset,int count){new[]{1f,1,0,0,1,0,0,0}.CopyTo(buffer,offset);return 8;}}
    private sealed class FakeAudio : IAudioCaptureService
    {
        public event Action<AudioFrame>? Frame { add { } remove { } }
        public event Action<string>? Failure { add { } remove { } }
        public IReadOnlyList<AudioDevice> Devices(bool mic) => []; public void Start(string mic, string? output, bool infer) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
