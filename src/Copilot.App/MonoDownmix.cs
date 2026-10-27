using NAudio.Wave;
namespace Copilot.App;
// Average all endpoint channels rather than silently selecting the last channel on surround devices.
public sealed class MonoDownmix(ISampleProvider source) : ISampleProvider
{
    private float[] scratch=[];
    public WaveFormat WaveFormat { get; }=WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate,1);
    public int Read(float[] buffer,int offset,int count)
    {
        int channels=source.WaveFormat.Channels;
        if(scratch.Length<count*channels)scratch=new float[count*channels];
        int read=source.Read(scratch,0,count*channels),frames=read/channels;
        for(int f=0;f<frames;f++){float sum=0;for(int c=0;c<channels;c++)sum+=scratch[f*channels+c];buffer[offset+f]=sum/channels;}
        return frames;
    }
}
