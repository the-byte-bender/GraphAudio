using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that delays audio by a specified time.
/// </summary>
public sealed class DelayNode : AudioNode
{
    private const int MaxDelaySeconds = 10;

    private CircularBuffer[] _delayBuffers;
    private AudioBuffer? _outputBuffer = null;
    private int _maxDelaySamples;

    /// <summary>
    /// The delay time in seconds.
    /// </summary>
    public AudioParam DelayTime { get; }

    public DelayNode(AudioContextBase context, double maxDelayTime = 1.0)
        : base(context, inputCount: 1, outputCount: 1, "Delay")
    {
        if (maxDelayTime <= 0 || maxDelayTime > MaxDelaySeconds)
            throw new ArgumentOutOfRangeException(nameof(maxDelayTime));

        _maxDelaySamples = (int)(maxDelayTime * context.SampleRate);
        _delayBuffers = new CircularBuffer[2];
        for (int i = 0; i < _delayBuffers.Length; i++)
        {
            _delayBuffers[i] = new CircularBuffer(_maxDelaySamples);
        }

        DelayTime = CreateAudioParam(
            name: "delayTime",
            defaultValue: 0.0f,
            minValue: 0.0f,
            maxValue: (float)maxDelayTime,
            automationRate: AutomationRate.ARate);
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;

        int inputChannels = input?.ChannelCount ?? 2;
        EnsureChannelCount(inputChannels);

        if (_outputBuffer is null || _outputBuffer.ChannelCount != inputChannels)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);

            _outputBuffer = Context.BufferPool.Rent(inputChannels);
        }

        bool hasAudio = false;
        var delayTimes = DelayTime.GetValues();

        if (input is null || input.IsSilent)
        {
            for (int ch = 0; ch < inputChannels; ch++)
            {
                var outSpan = _outputBuffer.GetChannelSpan(ch);
                for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
                {
                    int delaySamples = (int)(delayTimes[i] * Context.SampleRate);
                    delaySamples = Math.Clamp(delaySamples, 0, _maxDelaySamples);

                    outSpan[i] = _delayBuffers[ch].Read(delaySamples);
                    _delayBuffers[ch].Write(0f);
                    if (outSpan[i] != 0f) hasAudio = true;
                }
            }
        }
        else
        {
            for (int ch = 0; ch < inputChannels; ch++)
            {
                var inSpan = input.GetChannelSpan(ch);
                var outSpan = _outputBuffer.GetChannelSpan(ch);

                for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
                {
                    int delaySamples = (int)(delayTimes[i] * Context.SampleRate);
                    delaySamples = Math.Clamp(delaySamples, 0, _maxDelaySamples);

                    outSpan[i] = _delayBuffers[ch].Read(delaySamples);
                    _delayBuffers[ch].Write(inSpan[i]);
                    if (outSpan[i] != 0f) hasAudio = true;
                }
            }
        }

        if (hasAudio)
            _outputBuffer.MarkAsNonSilent();

        Outputs[0].SetBuffer(_outputBuffer);
    }

    private void EnsureChannelCount(int channels)
    {
        if (channels > _delayBuffers.Length)
        {
            int oldLen = _delayBuffers.Length;
            Array.Resize(ref _delayBuffers, channels);
            for (int i = oldLen; i < channels; i++)
            {
                _delayBuffers[i] = new CircularBuffer(_maxDelaySamples);
            }
        }
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }
    }

    private class CircularBuffer
    {
        private readonly float[] _buffer;
        private int _writePos;

        public CircularBuffer(int size)
        {
            _buffer = new float[size];
            _writePos = 0;
        }

        public void Write(float sample)
        {
            _buffer[_writePos] = sample;
            _writePos = (_writePos + 1) % _buffer.Length;
        }

        public float Read(int delaySamples)
        {
            if (delaySamples <= 0 || delaySamples > _buffer.Length)
                return 0f;

            int readPos = (_writePos - delaySamples + _buffer.Length) % _buffer.Length;
            return _buffer[readPos];
        }
    }
}
