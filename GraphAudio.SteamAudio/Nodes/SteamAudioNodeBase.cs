using System;
using GraphAudio.Core;
using GraphAudio.SteamAudio;
using SteamAudio;

namespace GraphAudio.Nodes;

public abstract unsafe class SteamAudioNodeBase : AudioNode, IDisposable
{
    protected readonly IPL.Context IplContext;
    private AudioBuffer? _outputBuffer;
    private IPL.AudioBuffer _iplInput;
    private IPL.AudioBuffer _iplOutput;
    private readonly IntPtr* _inputChannelPtrs;
    private readonly IntPtr* _outputChannelPtrs;
    private readonly int _maxInputChannelCount;
    private readonly int _outputChannelCount;

    protected SteamAudioNodeBase(
        AudioContextBase context,
        int maxInputChannelCount,
        int outputChannelCount,
        string? name = null)
        : base(context, inputCount: 1, outputCount: 1, name)
    {
        _maxInputChannelCount = maxInputChannelCount;
        _outputChannelCount = outputChannelCount;
        IplContext = context.GetSteamAudioContext();

        _inputChannelPtrs = (IntPtr*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(
            (nuint)maxInputChannelCount * (nuint)sizeof(IntPtr));
        _outputChannelPtrs = (IntPtr*)System.Runtime.InteropServices.NativeMemory.AllocZeroed(
            (nuint)outputChannelCount * (nuint)sizeof(IntPtr));

        _iplInput = new IPL.AudioBuffer
        {
            NumChannels = maxInputChannelCount,
            NumSamples = AudioBuffer.FramesPerBlock,
            Data = (IntPtr)_inputChannelPtrs
        };

        _iplOutput = new IPL.AudioBuffer
        {
            NumChannels = outputChannelCount,
            NumSamples = AudioBuffer.FramesPerBlock,
            Data = (IntPtr)_outputChannelPtrs
        };
    }

    protected override void Process()
    {
        var input = Inputs[0].Buffer;

        if (_outputBuffer is null || _outputBuffer.ChannelCount != _outputChannelCount)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(_outputChannelCount);
        }

        if (input.IsSilent)
        {
            _outputBuffer.Clear();
            SetOutputBuffer(0, _outputBuffer);
            return;
        }

        PinBuffersAndProcess(input, _outputBuffer);

        _outputBuffer.MarkAsNonSilent();
        SetOutputBuffer(0, _outputBuffer);
    }

    private void PinBuffersAndProcess(AudioBuffer input, AudioBuffer output)
    {
        int actualInputChannels = Math.Min(input.ChannelCount, _maxInputChannelCount);

        _iplInput.NumChannels = actualInputChannels;

        if (actualInputChannels == 1 && _outputChannelCount == 2)
        {
            var inputCh0 = input.GetChannelData(0);
            var outputCh0 = output.GetChannelData(0);
            var outputCh1 = output.GetChannelData(1);

            fixed (float* inputPtr0 = inputCh0)
            fixed (float* outputPtr0 = outputCh0)
            fixed (float* outputPtr1 = outputCh1)
            {
                _inputChannelPtrs[0] = (IntPtr)inputPtr0;
                _outputChannelPtrs[0] = (IntPtr)outputPtr0;
                _outputChannelPtrs[1] = (IntPtr)outputPtr1;

                ProcessSteamAudio(ref _iplInput, ref _iplOutput);
            }
        }
        else if (actualInputChannels == 1 && _outputChannelCount == 1)
        {
            var inputCh0 = input.GetChannelData(0);
            var outputCh0 = output.GetChannelData(0);

            fixed (float* inputPtr0 = inputCh0)
            fixed (float* outputPtr0 = outputCh0)
            {
                _inputChannelPtrs[0] = (IntPtr)inputPtr0;
                _outputChannelPtrs[0] = (IntPtr)outputPtr0;

                ProcessSteamAudio(ref _iplInput, ref _iplOutput);
            }
        }
        else if (actualInputChannels == 2 && _outputChannelCount == 2)
        {
            var inputCh0 = input.GetChannelData(0);
            var inputCh1 = input.GetChannelData(1);
            var outputCh0 = output.GetChannelData(0);
            var outputCh1 = output.GetChannelData(1);

            fixed (float* inputPtr0 = inputCh0)
            fixed (float* inputPtr1 = inputCh1)
            fixed (float* outputPtr0 = outputCh0)
            fixed (float* outputPtr1 = outputCh1)
            {
                _inputChannelPtrs[0] = (IntPtr)inputPtr0;
                _inputChannelPtrs[1] = (IntPtr)inputPtr1;
                _outputChannelPtrs[0] = (IntPtr)outputPtr0;
                _outputChannelPtrs[1] = (IntPtr)outputPtr1;

                ProcessSteamAudio(ref _iplInput, ref _iplOutput);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported channel configuration: {actualInputChannels} input -> {_outputChannelCount} output");
        }
    }

    protected abstract void ProcessSteamAudio(ref IPL.AudioBuffer input, ref IPL.AudioBuffer output);

    protected override void OnDispose()
    {
        if (_inputChannelPtrs != null)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_inputChannelPtrs);
        }
        if (_outputChannelPtrs != null)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_outputChannelPtrs);
        }

        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }

        base.OnDispose();
    }
}
