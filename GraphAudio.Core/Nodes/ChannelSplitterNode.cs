using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node to split multi-channel input into separate mono outputs.
/// </summary>
public sealed class ChannelSplitterNode : AudioNode
{
    private readonly AudioBuffer[] _outputBuffers;
    private readonly int _numberOfOutputs;

    public ChannelSplitterNode(AudioContextBase context, int numberOfOutputs = 2)
        : base(context, inputCount: 1, outputCount: numberOfOutputs, "ChannelSplitter")
    {
        if (numberOfOutputs < 1 || numberOfOutputs > 32)
            throw new ArgumentOutOfRangeException(nameof(numberOfOutputs));

        _numberOfOutputs = numberOfOutputs;
        _outputBuffers = new AudioBuffer[numberOfOutputs];
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;

        if (input is null || input.IsSilent)
        {
            for (int i = 0; i < _numberOfOutputs; i++)
            {
                if (_outputBuffers[i] is null)
                    _outputBuffers[i] = Context.BufferPool.Rent(1);

                _outputBuffers[i].Clear();
                Outputs[i].SetBuffer(_outputBuffers[i]);
            }
            return;
        }

        int inputChannels = input.ChannelCount;

        for (int i = 0; i < _numberOfOutputs; i++)
        {
            if (_outputBuffers[i] is null)
                _outputBuffers[i] = Context.BufferPool.Rent(1);

            if (i < inputChannels)
            {
                _outputBuffers[i].CopyChannelFrom(input, i, 0);
            }
            else
            {
                _outputBuffers[i].Clear();
            }

            Outputs[i].SetBuffer(_outputBuffers[i]);
        }
    }

    protected override void OnDispose()
    {
        for (int i = 0; i < _outputBuffers.Length; i++)
        {
            if (_outputBuffers[i] is not null)
            {
                Context.BufferPool.Return(_outputBuffers[i]);
            }
        }
    }
}
