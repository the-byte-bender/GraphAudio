using System;
using System.Numerics;

namespace GraphAudio.Kit;

internal readonly record struct StepLinearResult(float Pan, float Gain);

/// <summary>
/// Configuration for step-linear spatialization.
/// </summary>
public readonly record struct StepLinearConfig(float PanStep, float GainStep)
{
    /// <summary>
    /// Sensible default configuration for step-linear spatialization.
    /// </summary>
    public static readonly StepLinearConfig Default = new(PanStep: 0.05f, GainStep: 0.015f);
}

internal static class StepLinearCalculator
{
    private const float MinPan = -1.0f;
    private const float MaxPan = 1.0f;
    private const float MinGain = 0.0f;

    public static StepLinearResult Calculate(
        Vector3 listenerPosition,
        Vector3 sourcePosition,
        StepLinearConfig config,
        float initialPan = 0.0f,
        float initialGain = 0.0f
    )
    {
        float finalPan = initialPan;
        float finalGain = initialGain;
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
            Math.Max(finalGain, MinGain)
        );
    }
}
