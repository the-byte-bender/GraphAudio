using System;

namespace GraphAudio.Kit.SpatialBlendControllers;

/// <summary>
/// Expand immediately from 0 to 1 at any non-zero distance.
/// </summary>
public sealed class DefaultSpatialBlendController : ISpatialBlendController
{
    public static DefaultSpatialBlendController Instance { get; } = new();

    private const float Epsilon = 1e-4f;

    public float GetBlend(float distance)
    {
        return distance <= Epsilon ? 0f : 1f;
    }
}
