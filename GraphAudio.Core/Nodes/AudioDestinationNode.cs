using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// Represents the final audio output.
/// </summary>
public sealed class AudioDestinationNode : AudioNode
{
    private AudioBuffer? _outputBuffer;
    private bool _ownsOutputBuffer;

    public AudioDestinationNode(AudioContextBase context)
        : base(context, inputCount: 1, outputCount: 0, "AudioDestination")
    {
        Inputs[0].SetChannelCount(2);
    }

    /// <summary>
    /// Set the number of output channels (1 for mono, 2 for stereo, etc.).
    /// </summary>
    public void SetChannelCount(int channels)
    {
        if (channels < 1 || channels > 32)
            throw new ArgumentOutOfRangeException(nameof(channels));
        void Do(AudioContextBase _)
        {
            Inputs[0].SetChannelCount(channels);
        }
        Context.ExecuteOrPost(Do);
    }

    /// <summary>
    /// Get the processed output buffer. Only valid after ProcessBlock() is called.
    /// </summary>
    public AudioBuffer GetOutputBuffer()
    {
        return _outputBuffer!;
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;

        if (input is not null)
        {
            _outputBuffer = input;
            _ownsOutputBuffer = false;
        }
        else
        {
            if (_outputBuffer is null || _outputBuffer.ChannelCount != Inputs[0].ChannelCount || !_ownsOutputBuffer)
            {
                if (_outputBuffer is not null && _ownsOutputBuffer)
                    Context.BufferPool.Return(_outputBuffer);

                _outputBuffer = Context.BufferPool.Rent(Inputs[0].ChannelCount);
                _ownsOutputBuffer = true;
            }

            _outputBuffer.Clear();
        }
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null && _ownsOutputBuffer)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
            _ownsOutputBuffer = false;
        }
    }
}
