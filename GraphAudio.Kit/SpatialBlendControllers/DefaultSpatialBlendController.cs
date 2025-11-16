using System;

namespace GraphAudio.Kit.SpatialBlendControllers;

/// <summary>
/// A spatial blend controller that always returns 1.
/// </summary>
public sealed class DefaultSpatialBlendController : ISpatialBlendController
{
    public static DefaultSpatialBlendController Instance { get; } = new();

    public float GetBlend(float distance)
    {
        return 1f;
    }
}
