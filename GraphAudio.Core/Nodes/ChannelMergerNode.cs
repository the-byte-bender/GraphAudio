using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that combines multiple mono inputs into a multi-channel output.
/// </summary>
public sealed class ChannelMergerNode : AudioNode
{
    private readonly int _numberOfInputs;
    private AudioBuffer? _outputBuffer = null;

    public ChannelMergerNode(AudioContextBase context, int numberOfInputs = 2)
        : base(context, inputCount: numberOfInputs, outputCount: 1, "ChannelMerger")
    {
        if (numberOfInputs < 1 || numberOfInputs > 32)
            throw new ArgumentOutOfRangeException(nameof(numberOfInputs));

        _numberOfInputs = numberOfInputs;
    }

    protected override void Process()
    {
        if (_outputBuffer is null || _outputBuffer.ChannelCount != _numberOfInputs)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);

            _outputBuffer = Context.BufferPool.Rent(_numberOfInputs);
        }

        _outputBuffer.Clear();
        bool hasAudio = false;

        for (int i = 0; i < _numberOfInputs; i++)
        {
            var input = Inputs[i].Buffer;

            if (input is not null && !input.IsSilent)
            {
                int srcChannel = 0;
                if (srcChannel < input.ChannelCount)
                {
                    _outputBuffer.CopyChannelFrom(input, srcChannel, i);
                    hasAudio = true;
                }
            }
        }

        if (hasAudio)
            _outputBuffer.MarkAsNonSilent();

        Outputs[0].SetBuffer(_outputBuffer);
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }
    }
}
