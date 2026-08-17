namespace Playroma.Sound.Dsp.Modulation;

public interface IControlSource
{
    void Prepare(int sampleRate);

    void Reset();

    float NextValue();
}
