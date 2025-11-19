using System;
using System.Runtime.CompilerServices;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that performs linear convolution.
/// </summary>
public sealed class ConvolverNode : AudioNode
{
    private PlayableAudioBuffer? _buffer;
    private PartitionedConvolver[]? _convolvers;
    private AudioBuffer? _outputBuffer;
    private int _effectiveOutputChannels;
    private bool _isTrueStereo;

    /// <summary>
    /// An audio buffer containing the impulse response used by the ConvolverNode.
    /// </summary>
    /// <Remarks>
    /// The impulse response buffer's sample rate must match the audio context's sample rate or an exception will be thrown.
    /// Perhaps we can revisit this when we have better resamplers than cubic interpolation.
    /// </Remarks>
    public PlayableAudioBuffer? Buffer
    {
        get => _buffer;
        set
        {
            if (_buffer == value) return;

            if (value is null)
            {
                Context.Post(_ =>
                {
                    _buffer = null;
                    _convolvers = null;
                    _effectiveOutputChannels = 0;
                    _isTrueStereo = false;
                    Inputs[0].SetChannelCountMode(ChannelCountMode.Max);
                });
                return;
            }

            if (!value.IsInitialized)
                throw new InvalidOperationException("Impulse response buffer must be initialized before being assigned to the ConvolverNode.");

            if (value.SampleRate != Context.SampleRate)
                throw new InvalidOperationException($"Impulse response buffer sample rate must match the audio context sample rate. Impulse response buffer sample rate: {value.SampleRate}, Audio context sample rate: {Context.SampleRate}.");

            var newConvolvers = new PartitionedConvolver[value.NumberOfChannels];
            for (int i = 0; i < value.NumberOfChannels; i++)
            {
                var channelData = value.GetChannelData(i);
                newConvolvers[i] = new PartitionedConvolver(channelData, AudioBuffer.FramesPerBlock, Normalize);
            }

            Context.Post(_ =>
            {
                _buffer = value;
                _convolvers = newConvolvers;

                int channels = value.NumberOfChannels;
                _isTrueStereo = (channels == 4 && EnableTrueStereo);
                _effectiveOutputChannels = _isTrueStereo ? 2 : channels;

                if (_isTrueStereo)
                {
                    Inputs[0].SetChannelCount(2);
                    Inputs[0].SetChannelCountMode(ChannelCountMode.Explicit);
                }
                else
                {
                    Inputs[0].SetChannelCount(channels);
                    Inputs[0].SetChannelCountMode(ChannelCountMode.Explicit);
                }
            });
        }
    }

    /// <summary>
    /// Whether the impulse response from the buffer will be scaled by an equal-power normalization when the buffer attribute is set. Defaults to true.
    /// </summary>
    /// <Remarks>
    /// Setting this property will not re-process the existing impulse response buffer. To apply a change, re-set the Buffer property after changing this value.
    /// </Remarks>
    public bool Normalize { get; set; } = true;

    /// <summary>
    /// whether 4-channel impulse responses are treated as "True Stereo" or as discrete 4-channel convolution. Defaults to true.
    /// </summary>
    /// <Remarks>
    /// Setting this property will not re-process the existing impulse response buffer. To apply a change, re-set the Buffer property after changing this value.
    /// </Remarks>
    public bool EnableTrueStereo { get; set; } = true;

    public ConvolverNode(AudioContextBase context)
        : base(context, inputCount: 1, outputCount: 1, "Convolver")
    {
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;
        var convolvers = _convolvers;

        if (convolvers is null)
        {
            int ch = input.ChannelCount;
            if (_outputBuffer is null || _outputBuffer.ChannelCount != ch)
            {
                if (_outputBuffer is not null) Context.BufferPool.Return(_outputBuffer);
                _outputBuffer = Context.BufferPool.Rent(ch);
            }

            _outputBuffer.Clear();
            Outputs[0].SetBuffer(_outputBuffer);
            return;
        }

        if (_outputBuffer is null || _outputBuffer.ChannelCount != _effectiveOutputChannels)
        {
            if (_outputBuffer is not null) Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(_effectiveOutputChannels);
        }

        if (_isTrueStereo)
        {
            var inL = input.GetChannelSpan(0);
            var inR = input.GetChannelSpan(1);
            var outL = _outputBuffer.GetChannelSpan(0);
            var outR = _outputBuffer.GetChannelSpan(1);

            Span<float> temp1 = stackalloc float[AudioBuffer.FramesPerBlock];
            Span<float> temp2 = stackalloc float[AudioBuffer.FramesPerBlock];

            convolvers[0].Process(inL, temp1);
            convolvers[2].Process(inR, temp2);
            Sum(temp1, temp2, outL);

            convolvers[1].Process(inL, temp1);
            convolvers[3].Process(inR, temp2);
            Sum(temp1, temp2, outR);
        }
        else
        {
            for (int ch = 0; ch < _effectiveOutputChannels; ch++)
            {
                convolvers[ch].Process(input.GetChannelSpan(ch), _outputBuffer.GetChannelSpan(ch));
            }
        }

        Outputs[0].SetBuffer(_outputBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Sum(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> dst)
    {
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = a[i] + b[i];
        }
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }
        _convolvers = null;
        _buffer = null;
    }
}
