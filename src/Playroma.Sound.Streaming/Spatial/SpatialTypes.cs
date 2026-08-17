using System.Numerics;

namespace Playroma.Sound.Streaming.Spatial;

public readonly record struct SoundListenerState(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    Vector3 Velocity)
{
    public static SoundListenerState Default { get; } = new(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero);
}

public readonly record struct SoundEmitterState(
    bool Enabled,
    Vector3 Position,
    Vector3 Velocity,
    float MinimumDistance,
    float MaximumDistance,
    float Occlusion,
    float Obstruction)
{
    public static SoundEmitterState Disabled { get; } = new(false, Vector3.Zero, Vector3.Zero, 1f, 100f, 0f, 0f);
}

internal static class SimpleSpatializer
{
    public static void Calculate(
        in SoundListenerState listener,
        in SoundEmitterState emitter,
        out float gain,
        out float pan,
        out float doppler)
    {
        if (!emitter.Enabled)
        {
            gain = 1f;
            pan = 0f;
            doppler = 1f;
            return;
        }

        Vector3 offset = emitter.Position - listener.Position;
        float distance = offset.Length();
        float minimum = Math.Max(0.001f, emitter.MinimumDistance);
        float maximum = Math.Max(minimum + 0.001f, emitter.MaximumDistance);
        float normalized = Math.Clamp((distance - minimum) / (maximum - minimum), 0f, 1f);
        float distanceGain = 1f - normalized;
        distanceGain *= distanceGain;
        gain = distanceGain * (1f - Math.Clamp(emitter.Occlusion, 0f, 1f) * 0.75f)
            * (1f - Math.Clamp(emitter.Obstruction, 0f, 1f) * 0.35f);

        Vector3 right = Vector3.Cross(listener.Forward, listener.Up);
        if (right.LengthSquared() < 0.000001f) right = Vector3.UnitX;
        else right = Vector3.Normalize(right);
        Vector3 direction = distance > 0.0001f ? offset / distance : listener.Forward;
        pan = Math.Clamp(Vector3.Dot(direction, right), -1f, 1f);

        const float speedOfSound = 343f;
        float sourceRadial = Vector3.Dot(emitter.Velocity, direction);
        float listenerRadial = Vector3.Dot(listener.Velocity, direction);
        doppler = Math.Clamp((speedOfSound + listenerRadial) / Math.Max(1f, speedOfSound + sourceRadial), 0.5f, 2f);
    }
}
