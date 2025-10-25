using System;

namespace GraphAudio.Core;

/// <summary>
/// Interface for one-shot scheduled source nodes that can start/stop at precise times.
/// </summary>
public interface IAudioScheduledSourceNode
{
    /// <summary>
    /// Start playback at a given absolute time. If 'when' is in the past or 0, it starts immediately.
    /// </summary>
    void Start(double when = 0, double offset = 0, double duration = double.NaN);

    /// <summary>
    /// Stop playback at a given absolute time. If 'when' is in the past or 0, it stops immediately.
    /// This is final; The node cannot be restarted after stopping.
    /// </summary>
    void Stop(double when = 0);

    /// <summary>
    /// Raised exactly once when playback has ended. After this, the node is permanently ended.
    /// </summary>
    event EventHandler? Ended;
}
