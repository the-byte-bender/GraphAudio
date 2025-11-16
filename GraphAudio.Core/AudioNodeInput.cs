using System;
using System.Collections.Generic;
using GraphAudio.Nodes;

namespace GraphAudio.Core;

/// <summary>
/// Represents an input port on an audio node. Handles mixing multiple connections
/// and channel up/down-mixing.
/// </summary>
public sealed class AudioNodeInput
{
    private readonly AudioNode _owner;
    private readonly int _index;
    private readonly BufferPool _bufferPool;
    private readonly List<AudioNodeOutput> _connectedOutputs;
    private AudioBuffer? _buffer;
    private bool _bufferDirty = true;
    private int _channelCount = 2;
    private ChannelInterpretation _channelInterpretation = ChannelInterpretation.Speakers;
    private ChannelCountMode _channelCountMode = ChannelCountMode.Max;

    public AudioNode Owner => _owner;
    public int Index => _index;
    public int ChannelCount => _channelCount;
    public IReadOnlyList<AudioNodeOutput> ConnectedOutputs => _connectedOutputs;

    /// <summary>
    /// Get the mixed audio buffer for this input. Valid only during Process().
    /// </summary>
    public AudioBuffer Buffer => _buffer!;

    internal AudioNodeInput(AudioNode owner, int index, BufferPool bufferPool)
    {
        _owner = owner;
        _index = index;
        _bufferPool = bufferPool;
        _connectedOutputs = new List<AudioNodeOutput>(4);
    }

    public void SetChannelCount(int count)
    {
        if (count < 1 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count), "Channel count must be between 1 and 32");

        _channelCount = count;
        _bufferDirty = true;
    }

    public void SetChannelInterpretation(ChannelInterpretation interpretation)
    {
        _channelInterpretation = interpretation;
    }

    public void SetChannelCountMode(ChannelCountMode mode)
    {
        _channelCountMode = mode;
    }

    internal void AddConnection(AudioNodeOutput output)
    {
        if (!_connectedOutputs.Contains(output))
        {
            _connectedOutputs.Add(output);
            _bufferDirty = true;
        }
    }

    internal void RemoveConnection(AudioNodeOutput output)
    {
        _connectedOutputs.Remove(output);
        _bufferDirty = true;
    }

    internal void DisconnectAll()
    {
        var outputs = _connectedOutputs.ToArray();
        foreach (var output in outputs)
        {
            output.DisconnectFrom(this);
        }
        _bufferDirty = true;
    }

    /// <summary>
    /// Clean up resources, returning the buffer to the pool.
    /// </summary>
    internal void Dispose()
    {
        if (_buffer != null)
        {
            _bufferPool.Return(_buffer);
            _buffer = null;
        }
    }

    /// <summary>
    /// Pull audio from all connected outputs and mix them together.
    /// </summary>
    internal void Pull(int blockNumber, double blockTime)
    {
        if (_connectedOutputs.Count == 0)
        {
            EnsureBuffer();
            _buffer?.Clear();
            return;
        }

        int outputChannels = ComputeOutputChannelCount();

        EnsureBuffer();
        if (_buffer?.ChannelCount != outputChannels && _buffer is not null)
        {
            _bufferPool.Return(_buffer);
            _buffer = _bufferPool.Rent(outputChannels);
        }

        _buffer?.Clear();

        bool mixedAnyAudio = false;
        for (int i = 0; i < _connectedOutputs.Count; i++)
        {
            var output = _connectedOutputs[i];
            output.ProcessIfNeeded(blockNumber, blockTime);
            var sourceBuffer = output.Buffer;

            if (sourceBuffer != null && !sourceBuffer.IsSilent && _buffer is not null)
            {
                MixBuffer(sourceBuffer, _buffer, _channelInterpretation);
                mixedAnyAudio = true;
            }
        }

        if (mixedAnyAudio && _buffer is not null)
        {
            _buffer.MarkAsNonSilent();
        }
    }

    private int ComputeOutputChannelCount()
    {
        switch (_channelCountMode)
        {
            case ChannelCountMode.Explicit:
                return _channelCount;

            case ChannelCountMode.ClampedMax:
                int maxChannels = 0;
                for (int i = 0; i < _connectedOutputs.Count; i++)
                {
                    var output = _connectedOutputs[i];
                    if (output.Buffer is not null)
                        maxChannels = Math.Max(maxChannels, output.Buffer.ChannelCount);
                }
                return Math.Min(maxChannels == 0 ? _channelCount : maxChannels, _channelCount);

            case ChannelCountMode.Max:
            default:
                int max = _channelCount;
                for (int i = 0; i < _connectedOutputs.Count; i++)
                {
                    var output = _connectedOutputs[i];
                    if (output.Buffer is not null)
                        max = Math.Max(max, output.Buffer.ChannelCount);
                }
                return max;
        }
    }

    private void EnsureBuffer()
    {
        if (_buffer is null || _bufferDirty)
        {
            if (_buffer is not null)
                _bufferPool.Return(_buffer);

            _buffer = _bufferPool.Rent(_channelCount);
            _bufferDirty = false;
        }
    }

    private static void MixBuffer(AudioBuffer source, AudioBuffer destination, ChannelInterpretation interpretation)
    {
        int srcChannels = source.ChannelCount;
        int dstChannels = destination.ChannelCount;
        int frameCount = AudioBuffer.FramesPerBlock;

        if (srcChannels == dstChannels)
        {
            for (int ch = 0; ch < srcChannels; ch++)
            {
                var srcSpan = source.GetChannelSpan(ch);
                var dstSpan = destination.GetChannelSpan(ch);

                for (int i = 0; i < frameCount; i++)
                {
                    dstSpan[i] += srcSpan[i];
                }
            }
        }
        else if (srcChannels == 1 && dstChannels > 1)
        {
            var srcSpan = source.GetChannelSpan(0);

            for (int ch = 0; ch < dstChannels; ch++)
            {
                var dstSpan = destination.GetChannelSpan(ch);
                for (int i = 0; i < frameCount; i++)
                {
                    dstSpan[i] += srcSpan[i];
                }
            }
        }
        else if (srcChannels > 1 && dstChannels == 1)
        {
            var dstSpan = destination.GetChannelSpan(0);
            float scale = 1.0f / MathF.Sqrt(srcChannels);

            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < srcChannels; ch++)
                {
                    sum += source.GetChannelSpan(ch)[i];
                }
                dstSpan[i] += sum * scale;
            }
        }
        else
        {
            int minChannels = Math.Min(srcChannels, dstChannels);

            for (int ch = 0; ch < minChannels; ch++)
            {
                var srcSpan = source.GetChannelSpan(ch);
                var dstSpan = destination.GetChannelSpan(ch);

                for (int i = 0; i < frameCount; i++)
                {
                    dstSpan[i] += srcSpan[i];
                }
            }
        }
    }
}

public enum ChannelInterpretation
{
    /// <summary>
    /// Map channels according to standard speaker positions (e.g. stereo left/right, 5.1, etc).
    /// </summary>
    Speakers,
    /// <summary>
    /// Treat channels independently
    /// </summary>
    Discrete
}

public enum ChannelCountMode
{
    /// <summary>
    /// Use the maximum of all input channel counts
    /// </summary>
    Max,
    /// <summary>
    /// Use the maximum of all input channel counts, clamped to the node's channelCount.
    /// </summary>
    ClampedMax,
    /// <summary>
    /// Always use the node's channelCount.
    /// </summary>
    Explicit
}
