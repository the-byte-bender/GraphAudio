using System;

namespace GraphAudio.Kit.SpatialBlendControllers;

/// <summary>
/// Computes a spatial blend that increases linearly between a minimum and maximum distance and a configurable minimum and maximum blend value.
/// </summary>
public sealed class LinearSpatialBlendController : ISpatialBlendController
{
    private readonly float _minDistance;
    private readonly float _maxDistance;
    private readonly float _minBlend;
    private readonly float _maxBlend;
    private readonly float _distanceRange;
    private readonly float _blendRange;

    public LinearSpatialBlendController(
        float minDistance,
        float maxDistance,
        float minBlend = 0.0f,
        float maxBlend = 1.0f)
    {
        if (minDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(minDistance), "Distance cannot be negative.");
        if (maxDistance <= minDistance)
            throw new ArgumentException("maxDistance must be greater than minDistance.", nameof(maxDistance));

        if (minBlend < 0.0f || minBlend > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(minBlend), "minBlend must be between 0.0 and 1.0.");
        if (maxBlend < 0.0f || maxBlend > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(maxBlend), "maxBlend must be between 0.0 and 1.0.");
        if (maxBlend < minBlend)
            throw new ArgumentException("maxBlend cannot be less than minBlend.", nameof(maxBlend));

        _minDistance = minDistance;
        _maxDistance = maxDistance;
        _minBlend = minBlend;
        _maxBlend = maxBlend;
        _distanceRange = maxDistance - minDistance;
        _blendRange = maxBlend - minBlend;
    }

    public float GetBlend(float distance)
    {
        if (distance <= _minDistance)
        {
            return _minBlend;
        }

        if (distance >= _maxDistance)
        {
            return _maxBlend;
        }

        float t = (distance - _minDistance) / _distanceRange;

        return _minBlend + (t * _blendRange);
    }
}
