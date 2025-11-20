using System;
using System.Collections.Generic;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// Wraps a single AudioNode as an Effect.
/// </summary>
public sealed class NodeEffect : Effect
{
    private readonly AudioNode _node;

    /// <summary>
    /// The underlying audio node.
    /// </summary>
    public AudioNode Node => _node;

    public override AudioNode Input => _node;

    public override AudioNode Output => _node;

    public NodeEffect(AudioEngine engine, AudioNode node) : base(engine)
    {
        if (node.Context != engine.Context)
            throw new ArgumentException("Node must belong to the engine's context");
        _node = node;
    }

    protected override void OnDispose()
    {
        _node.Dispose();
    }
}
