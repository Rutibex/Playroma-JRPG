namespace Playroma.Sound.Core.Envelopes;

public interface IEnvelope
{
    bool IsComplete { get; }

    void Prepare(int sampleRate);

    void Reset();

    float NextValue();
}
