using System.Numerics;
using System.Threading;

namespace GraphAudio.Kit;

/// <summary>
/// Represents a spatial anchor for sounds in 3D space
/// </summary>
public sealed class SpatialAnchor
{
    private Vector3 _position;
    private ulong _version = 1;

    /// <summary>
    /// The position of the spatial anchor
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                Interlocked.Increment(ref _version);
            }
        }
    }

    internal ulong Version => _version;
}
