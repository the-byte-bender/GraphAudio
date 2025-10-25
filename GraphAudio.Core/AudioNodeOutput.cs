using System;
using System.Collections.Generic;
using GraphAudio.Nodes;

namespace GraphAudio.Core;

/// <summary>
/// Represents an output port on an audio node. 
/// </summary>
public sealed class AudioNodeOutput
{
    private readonly AudioNode _owner;
    private readonly int _index;
    private readonly List<AudioNodeInput> _connectedInputs;

    private AudioBuffer? _buffer = null;

    public AudioNode Owner => _owner;
    public int Index => _index;
    public IReadOnlyList<AudioNodeInput> ConnectedInputs => _connectedInputs;

    /// <summary>
    /// Get the audio buffer for this output. Valid only during/after Process().
    /// </summary>
    public AudioBuffer? Buffer => _buffer;

    internal AudioNodeOutput(AudioNode owner, int index)
    {
        _owner = owner;
        _index = index;
        _connectedInputs = new List<AudioNodeInput>(4);
    }

    /// <summary>
    /// Set the buffer for this output. Called by the node during processing.
    /// </summary>
    internal void SetBuffer(AudioBuffer buffer)
    {
        _buffer = buffer;
    }

    internal void ConnectTo(AudioNodeInput input)
    {
        if (input.Owner == _owner)
            throw new InvalidOperationException("Cannot connect a node to itself");

        if (!_connectedInputs.Contains(input))
        {
            _connectedInputs.Add(input);
            input.AddConnection(this);
        }
    }

    internal void DisconnectFrom(AudioNodeInput input)
    {
        if (_connectedInputs.Remove(input))
        {
            input.RemoveConnection(this);
        }
    }

    internal void DisconnectAll()
    {
        for (int i = _connectedInputs.Count - 1; i >= 0; i--)
        {
            var input = _connectedInputs[i];
            input.RemoveConnection(this);
            _connectedInputs.RemoveAt(i);
        }
    }

    /// <summary>
    /// Ensure the owning node has been processed for this block.
    /// </summary>
    internal void ProcessIfNeeded(int blockNumber, double blockTime)
    {
        _owner.ProcessInternal(blockNumber, blockTime);
    }
}
