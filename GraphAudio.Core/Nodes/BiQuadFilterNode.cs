using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// Biquad filter node implementing standard second-order IIR filters.
/// Supports lowpass, highpass, bandpass, notch, and more.
/// </summary>
public sealed class BiQuadFilterNode : AudioNode
{
    private FilterType _type = FilterType.Lowpass;
    private float _lastFrequency = 1000f;
    private float _lastQ = 1.0f;
    private float _lastGain = 0f;
    private float _b0, _b1, _b2, _a1, _a2;
    private bool _coefficientsDirty = true;
    private FilterState[] _channelStates;
    private AudioBuffer? _outputBuffer = null;

    public FilterType Type
    {
        get => _type;
        set
        {
            var newType = value;
            void Do(AudioContextBase _)
            {
                if (_type != newType)
                {
                    _type = newType;
                    _coefficientsDirty = true;
                }
            }
            Context.ExecuteOrPost(Do);
        }
    }

    /// <summary>
    /// Filter cutoff/center frequency in Hz.
    /// </summary>
    public AudioParam Frequency { get; }

    /// <summary>
    /// Quality factor.
    /// </summary>
    public AudioParam Q { get; }

    /// <summary>
    /// Gain in dB for peaking/shelf filters.
    /// </summary>
    public AudioParam Gain { get; }

    public BiQuadFilterNode(AudioContextBase context)
        : base(context, inputCount: 1, outputCount: 1, "BiQuadFilter")
    {
        _channelStates = new FilterState[2];
        for (int i = 0; i < _channelStates.Length; i++)
        {
            _channelStates[i] = new FilterState();
        }

        Frequency = CreateAudioParam(
            name: "frequency",
            defaultValue: 1000f,
            minValue: 1f,
            maxValue: context.SampleRate / 2f,
            automationRate: AutomationRate.ARate);

        Q = CreateAudioParam(
            name: "Q",
            defaultValue: 1.0f,
            minValue: 0.001f,
            maxValue: 1000f,
            automationRate: AutomationRate.ARate);

        Gain = CreateAudioParam(
            name: "gain",
            defaultValue: 0f,
            minValue: -60f,
            maxValue: 60f,
            automationRate: AutomationRate.KRate);

        UpdateCoefficients(_lastFrequency, _lastQ, _lastGain);
    }

    protected override void Process()
    {
        var freqValues = Frequency.GetValues();
        var qValues = Q.GetValues();
        var gainDb = Gain.GetValues()[0];

        var input = Inputs[0].Buffer;
        int channels = input.ChannelCount;
        EnsureChannelStates(channels);
        if (_outputBuffer is null || _outputBuffer.ChannelCount != channels)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(channels);
        }

        if (input.IsSilent)
        {
            _outputBuffer.Clear();
            Outputs[0].SetBuffer(_outputBuffer);
            return;
        }

        float lastB0 = _b0, lastB1 = _b1, lastB2 = _b2, lastA1 = _a1, lastA2 = _a2;
        float usedFreq = _lastFrequency;
        float usedQ = _lastQ;
        float usedGain = gainDb;

        for (int ch = 0; ch < channels; ch++)
        {
            var inSpan = input.GetChannelSpan(ch);
            var outSpan = _outputBuffer.GetChannelSpan(ch);
            ref var state = ref _channelStates[ch];

            for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
            {
                float f = Math.Clamp(freqValues[i], 1f, Context.SampleRate / 2f);
                float q = Math.Max(0.001f, qValues[i]);

                if (_coefficientsDirty || Math.Abs(f - usedFreq) > 0.001f || Math.Abs(q - usedQ) > 0.0001f || Math.Abs(gainDb - usedGain) > 0.001f)
                {
                    UpdateCoefficients(f, q, gainDb);
                    usedFreq = f;
                    usedQ = q;
                    usedGain = gainDb;
                    _coefficientsDirty = false;
                    lastB0 = _b0; lastB1 = _b1; lastB2 = _b2; lastA1 = _a1; lastA2 = _a2;
                }

                float x = inSpan[i];
                float w = x - lastA1 * state.W1 - lastA2 * state.W2;
                float y = lastB0 * w + lastB1 * state.W1 + lastB2 * state.W2;
                state.W2 = state.W1;
                state.W1 = w;
                outSpan[i] = y;
            }
        }

        _outputBuffer.MarkAsNonSilent();
        Outputs[0].SetBuffer(_outputBuffer);
    }

    private void UpdateCoefficients(float frequency, float q, float gain)
    {
        float w0 = 2f * MathF.PI * frequency / Context.SampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2f * q);

        float a0, a1, a2, b0, b1, b2;

        switch (_type)
        {
            case FilterType.Lowpass:
                b0 = (1f - cosW0) / 2f;
                b1 = 1f - cosW0;
                b2 = (1f - cosW0) / 2f;
                a0 = 1f + alpha;
                a1 = -2f * cosW0;
                a2 = 1f - alpha;
                break;

            case FilterType.Highpass:
                b0 = (1f + cosW0) / 2f;
                b1 = -(1f + cosW0);
                b2 = (1f + cosW0) / 2f;
                a0 = 1f + alpha;
                a1 = -2f * cosW0;
                a2 = 1f - alpha;
                break;

            case FilterType.Bandpass:
                b0 = alpha;
                b1 = 0f;
                b2 = -alpha;
                a0 = 1f + alpha;
                a1 = -2f * cosW0;
                a2 = 1f - alpha;
                break;

            case FilterType.Notch:
                b0 = 1f;
                b1 = -2f * cosW0;
                b2 = 1f;
                a0 = 1f + alpha;
                a1 = -2f * cosW0;
                a2 = 1f - alpha;
                break;

            case FilterType.Allpass:
                b0 = 1f - alpha;
                b1 = -2f * cosW0;
                b2 = 1f + alpha;
                a0 = 1f + alpha;
                a1 = -2f * cosW0;
                a2 = 1f - alpha;
                break;

            case FilterType.Peaking:
                {
                    float A = MathF.Pow(10f, gain / 40f);
                    b0 = 1f + alpha * A;
                    b1 = -2f * cosW0;
                    b2 = 1f - alpha * A;
                    a0 = 1f + alpha / A;
                    a1 = -2f * cosW0;
                    a2 = 1f - alpha / A;
                    break;
                }

            case FilterType.Lowshelf:
                {
                    float A = MathF.Pow(10f, gain / 40f);
                    float sqrtA = MathF.Sqrt(A);
                    float beta = sqrtA / q;

                    b0 = A * ((A + 1f) - (A - 1f) * cosW0 + beta * sinW0);
                    b1 = 2f * A * ((A - 1f) - (A + 1f) * cosW0);
                    b2 = A * ((A + 1f) - (A - 1f) * cosW0 - beta * sinW0);
                    a0 = (A + 1f) + (A - 1f) * cosW0 + beta * sinW0;
                    a1 = -2f * ((A - 1f) + (A + 1f) * cosW0);
                    a2 = (A + 1f) + (A - 1f) * cosW0 - beta * sinW0;
                    break;
                }

            case FilterType.Highshelf:
                {
                    float A = MathF.Pow(10f, gain / 40f);
                    float sqrtA = MathF.Sqrt(A);
                    float beta = sqrtA / q;

                    b0 = A * ((A + 1f) + (A - 1f) * cosW0 + beta * sinW0);
                    b1 = -2f * A * ((A - 1f) + (A + 1f) * cosW0);
                    b2 = A * ((A + 1f) + (A - 1f) * cosW0 - beta * sinW0);
                    a0 = (A + 1f) - (A - 1f) * cosW0 + beta * sinW0;
                    a1 = 2f * ((A - 1f) - (A + 1f) * cosW0);
                    a2 = (A + 1f) - (A - 1f) * cosW0 - beta * sinW0;
                    break;
                }

            default:
                b0 = 1f; b1 = 0f; b2 = 0f;
                a0 = 1f; a1 = 0f; a2 = 0f;
                break;
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    private void EnsureChannelStates(int channels)
    {
        if (channels > _channelStates.Length)
        {
            Array.Resize(ref _channelStates, channels);
            for (int i = _channelStates.Length; i < channels; i++)
            {
                _channelStates[i] = new FilterState();
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

    private struct FilterState
    {
        public float W1;
        public float W2;
    }
}

public enum FilterType
{
    Lowpass,
    Highpass,
    Bandpass,
    Notch,
    Allpass,
    Peaking,
    Lowshelf,
    Highshelf
}
