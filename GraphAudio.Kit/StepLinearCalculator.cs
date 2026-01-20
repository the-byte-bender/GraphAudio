using System;
using System.Numerics;

namespace GraphAudio.Kit;

internal readonly record struct StepLinearResult(float Pan, float Gain, float Pitch);

/// <summary>
/// Configuration for step-linear spatialization.
/// </summary>
public readonly record struct StepLinearConfig(
    float PanStep,
    float GainStep,
    float BehindPitchDecrease = 0f,
    float PitchLowerLimit = 0.01f
)
{
    /// <summary>
    /// Sensible default configuration for step-linear spatialization.
    /// </summary>
    public static readonly StepLinearConfig Default = new(
        PanStep: 0.1f,
        GainStep: 0.05f,
        BehindPitchDecrease: 0.015f,
        PitchLowerLimit: 0.1f);
}

internal static class StepLinearCalculator
{
    private const float MinPan = -1.0f;
    private const float MaxPan = 1.0f;
    private const float MinGain = -1.0f;
    private const float DefaultPitch = 1.0f;

    public static StepLinearResult Calculate(
        Vector3 listenerPosition,
        Vector3 sourcePosition,
        StepLinearConfig config,
        float initialPan = 0.0f,
        float initialGain = 0.0f,
        float initialPitch = DefaultPitch)
    {
        float finalPan = initialPan;
        float finalGain = initialGain;
        float finalPitch = initialPitch;

        if (sourcePosition.X < listenerPosition.X)
        {
            float deltaX = listenerPosition.X - sourcePosition.X;
            finalPan -= (deltaX * config.PanStep);
            finalGain -= (deltaX * config.GainStep);
        }
        else if (sourcePosition.X > listenerPosition.X)
        {
            float deltaX = sourcePosition.X - listenerPosition.X;
            finalPan += (deltaX * config.PanStep);
            finalGain -= (deltaX * config.GainStep);
        }

        if (sourcePosition.Y < listenerPosition.Y)
        {
            finalPitch -= MathF.Abs(config.BehindPitchDecrease);
            float deltaY = listenerPosition.Y - sourcePosition.Y;
            finalGain -= (deltaY * config.GainStep);
        }
        else if (sourcePosition.Y > listenerPosition.Y)
        {
            float deltaY = sourcePosition.Y - listenerPosition.Y;
            finalGain -= (deltaY * config.GainStep);
        }

        if (sourcePosition.Z < listenerPosition.Z)
        {
            finalPitch -= MathF.Abs(config.BehindPitchDecrease);
            float deltaZ = listenerPosition.Z - sourcePosition.Z;
            finalGain -= (deltaZ * config.GainStep);
        }
        else if (sourcePosition.Z > listenerPosition.Z)
        {
            float deltaZ = sourcePosition.Z - listenerPosition.Z;
            finalGain -= (deltaZ * config.GainStep);
        }

        return new StepLinearResult(
            Math.Clamp(finalPan, MinPan, MaxPan),
            Math.Max(finalGain, MinGain),
            Math.Max(finalPitch, config.PitchLowerLimit)
        );
    }
}
