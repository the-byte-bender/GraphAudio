using System;
using System.Collections.Generic;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// Represents an audio bus for mixing and routing audio signals.
/// Buses can be organized hierarchically, with all buses eventually routing to the master bus.
/// </summary>
public sealed class AudioBus
{
    private float _gain = 1.0f;
    private bool _muted = false;
    private readonly List<AudioBus> _childBuses = new();
    private readonly GainNode _gainNode;

    /// <summary>
    /// The audio engine this bus belongs to.
    /// </summary>
    public AudioEngine Engine { get; }

    /// <summary>
    /// The full path of this bus (e.g., "master", "sfx", "music/menu").
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The parent bus, or null if this is the master bus.
    /// </summary>
    public AudioBus? Parent { get; }

    /// <summary>
    /// Whether this is the master bus.
    /// </summary>
    public bool IsMaster => Parent is null;

    /// <summary>
    /// The gain of this bus.
    /// </summary>
    public float Gain
    {
        get => _gain;
        set
        {
            _gain = Math.Clamp(value, 0f, 1f);
            UpdateGain();
        }
    }

    /// <summary>
    /// Whether this bus is muted.
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            UpdateGain();
        }
    }

    /// <summary>
    /// The child buses of this bus.
    /// </summary>
    public IReadOnlyList<AudioBus> Children => _childBuses;

    /// <summary>
    /// The effect chain for this bus.
    /// </summary>
    public EffectChain Effects { get; }

    internal AudioNode Input => _gainNode;

    internal AudioBus(AudioEngine engine, string path, AudioBus? parent)
    {
        Engine = engine;
        Path = path;
        Parent = parent;

        _gainNode = new GainNode(engine.Context);

        var destination = parent?.Input ?? engine.Context.Destination;
        Effects = new EffectChain(engine.Context, _gainNode, destination);

        if (parent is not null)
        {
            parent._childBuses.Add(this);
        }
    }

    /// <summary>
    /// Fade the gain to a target value over a specified duration.
    /// </summary>
    public void Fade(float target, double duration)
    {
        target = Math.Clamp(target, 0f, 1f);

        if (duration <= 0)
        {
            Gain = target;
            return;
        }

        var currentTime = Engine.Context.CurrentTime;

        var currentValue = Math.Max(_gain, 0.0001f);
        var targetValue = Math.Max(target, 0.0001f);

        _gainNode.Gain.SetValueAtTime(currentValue, currentTime);
        _gainNode.Gain.ExponentialRampToValueAtTime(targetValue, currentTime + duration);
        _gain = target;
    }

    private void UpdateGain()
    {
        _gainNode.Gain.Value = _muted ? 0f : _gain;
    }

    internal void Disconnect()
    {
        _gainNode.Disconnect();
        _gainNode.Dispose();
    }
}
