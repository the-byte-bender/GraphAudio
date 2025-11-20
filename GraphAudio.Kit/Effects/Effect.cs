using System;
using System.Collections.Generic;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// Base class for high-level audio effects.
/// </summary>
public abstract class Effect : IDisposable
{
    /// <summary>
    /// The audio engine this effect belongs to.
    /// </summary>
    public AudioEngine Engine { get; }

    /// <summary>
    /// The audio context this effect belongs to.
    /// </summary>
    public AudioContextBase Context => Engine.Context;

    /// <summary>
    /// The entry point node for this effect.
    /// The chain will connect the previous node's output to this node.
    /// </summary>
    public abstract AudioNode Input { get; }

    /// <summary>
    /// The exit point node for this effect.
    /// The chain will connect this node's output to the next node.
    /// </summary>
    public abstract AudioNode Output { get; }

    protected Effect(AudioEngine engine)
    {
        Engine = engine;
    }

    /// <summary>
    /// Disposes the effect and its internal nodes.
    /// </summary>
    public void Dispose()
    {
        OnDispose();
    }

    protected abstract void OnDispose();
}
