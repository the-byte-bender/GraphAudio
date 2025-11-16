using System;

namespace GraphAudio.Kit.SpatialBlendControllers;

/// <summary>
/// Computes a spatial blend  from a distance value.
/// </summary>
public interface ISpatialBlendController
{
    /// <summary>
    /// Returns a spatial blend value in the range [0..1] for the given distance.
    /// </summary>
    float GetBlend(float distance);
}
