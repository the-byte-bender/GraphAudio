using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that multiplies input audio by a gain factor.
/// </summary>
public sealed class GainNode : AudioNode
{
    private AudioBuffer? _outputBuffer = null;

    /// <summary>
    /// The gain value.
    /// </summary>
    public AudioParam Gain { get; }

    public GainNode(AudioContextBase context)
        : base(context, inputCount: 1, outputCount: 1, "Gain")
    {
        Gain = CreateAudioParam(
            name: "gain",
            defaultValue: 1.0f,
            minValue: float.MinValue,
            maxValue: float.MaxValue,
            automationRate: AutomationRate.ARate);
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;

        if (_outputBuffer is null || _outputBuffer.ChannelCount != input.ChannelCount)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);

            _outputBuffer = Context.BufferPool.Rent(input.ChannelCount);
        }

        if (input.IsSilent)
        {
            _outputBuffer.Clear();
            Outputs[0].SetBuffer(_outputBuffer);
            return;
        }

        var gainValues = Gain.GetValues();
        _outputBuffer.CopyFrom(input);

        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var channelSpan = _outputBuffer.GetChannelSpan(ch);
            for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
            {
                channelSpan[i] *= gainValues[i];
            }
        }

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
