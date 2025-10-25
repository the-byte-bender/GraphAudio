using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that positions an incoming audio stream in a stereo image.
/// </summary>
public sealed class StereoPannerNode : AudioNode
{
    private AudioBuffer? _outputBuffer = null;
    private float _lastPan = float.NaN;
    private float _lastGainL = 0.5f;
    private float _lastGainR = 0.5f;

    /// <summary>
    /// The pan position. Values range from -1 (full left) to 1 (full right), with 0 being center.
    /// </summary>
    public AudioParam Pan { get; }

    public StereoPannerNode(AudioContextBase context)
        : base(context, inputCount: 1, outputCount: 1, "StereoPanner")
    {
        Inputs[0].SetChannelCount(2);
        Inputs[0].SetChannelCountMode(ChannelCountMode.ClampedMax);
        Inputs[0].SetChannelInterpretation(ChannelInterpretation.Speakers);

        Pan = CreateAudioParam(
            name: "pan",
            defaultValue: 0.0f,
            minValue: -1.0f,
            maxValue: 1.0f,
            automationRate: AutomationRate.ARate);
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;
        int inputChannels = input.ChannelCount;

        if (_outputBuffer is null || _outputBuffer.ChannelCount != 2)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);

            _outputBuffer = Context.BufferPool.Rent(2);
        }

        if (input.IsSilent)
        {
            _outputBuffer.Clear();
            Outputs[0].SetBuffer(_outputBuffer);
            return;
        }

        var panValues = Pan.GetValues();
        var outputL = _outputBuffer.GetChannelSpan(0);
        var outputR = _outputBuffer.GetChannelSpan(1);

        if (inputChannels == 1)
        {
            var inputData = input.GetChannelSpan(0);
            ProcessMono(inputData, outputL, outputR, panValues);
        }
        else if (inputChannels >= 2)
        {
            var inputL = input.GetChannelSpan(0);
            var inputR = input.GetChannelSpan(1);
            ProcessStereo(inputL, inputR, outputL, outputR, panValues);
        }

        _outputBuffer.MarkAsNonSilent();
        Outputs[0].SetBuffer(_outputBuffer);
    }

    private void ProcessMono(
        ReadOnlySpan<float> input,
        Span<float> outputL,
        Span<float> outputR,
        ReadOnlySpan<float> panValues)
    {
        float gainL = _lastGainL;
        float gainR = _lastGainR;
        float lastPan = _lastPan;

        for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
        {
            float pan = Math.Clamp(panValues[i], -1.0f, 1.0f);

            if (pan != lastPan)
            {
                float x = (pan + 1.0f) * 0.5f;

                gainL = MathF.Cos(x * MathF.PI / 2.0f);
                gainR = MathF.Sin(x * MathF.PI / 2.0f);

                lastPan = pan;
            }

            float sample = input[i];
            outputL[i] = sample * gainL;
            outputR[i] = sample * gainR;
        }

        _lastPan = lastPan;
        _lastGainL = gainL;
        _lastGainR = gainR;
    }

    private void ProcessStereo(
        ReadOnlySpan<float> inputL,
        ReadOnlySpan<float> inputR,
        Span<float> outputL,
        Span<float> outputR,
        ReadOnlySpan<float> panValues)
    {
        float gainL = _lastGainL;
        float gainR = _lastGainR;
        float lastPan = _lastPan;

        for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
        {
            float pan = Math.Clamp(panValues[i], -1.0f, 1.0f);

            if (pan != lastPan)
            {
                float x = pan <= 0.0f ? pan + 1.0f : pan;

                gainL = MathF.Cos(x * MathF.PI / 2.0f);
                gainR = MathF.Sin(x * MathF.PI / 2.0f);

                lastPan = pan;
            }

            float inL = inputL[i];
            float inR = inputR[i];

            if (pan <= 0.0f)
            {
                outputL[i] = inL + inR * gainL;
                outputR[i] = inR * gainR;
            }
            else
            {
                outputL[i] = inL * gainL;
                outputR[i] = inR + inL * gainR;
            }
        }

        _lastPan = lastPan;
        _lastGainL = gainL;
        _lastGainR = gainR;
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
