using System;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Nodes;

/// <summary>
/// An audio source node that plays back audio from a PlayableAudioBuffer.
/// </summary>
/// <remarks>
/// This node automatically disposes itself when playback ends.
/// </remarks>
public sealed class AudioBufferSourceNode : AudioNode, IAudioScheduledSourceNode
{
    private PlayableAudioBuffer? _buffer;
    private bool _hasStarted = false;
    private bool _hasStopped = false;
    private bool _endedEventRaised = false;
    private double _startTime = double.NaN;
    private double _stopTime = double.NaN;
    private double _offset;
    private double _duration = double.PositiveInfinity;
    private long _playbackPosition;
    private bool _loop;
    private double _loopStart;
    private double _loopEnd;

    private AudioBuffer? _outputBuffer;
    private CubicResampler[]? _resamplers;
    private float[]? _loopWrapBuffer;

    public AudioParam PlaybackRate { get; }

    public event EventHandler? Ended;

    /// <summary>
    /// Gets or sets whether the audio should loop.
    /// </summary>
    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    /// <summary>
    /// Gets or sets the loop start point in seconds.
    /// </summary>
    public double LoopStart
    {
        get => _loopStart;
        set => _loopStart = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets the loop end point in seconds (0 means end of buffer).
    /// </summary>
    public double LoopEnd
    {
        get => _loopEnd;
        set => _loopEnd = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets the audio buffer to play.
    /// Can only be set once before playback starts.
    /// </summary>
    public PlayableAudioBuffer? Buffer
    {
        get => _buffer;
        set => _buffer = value;
    }

    public AudioBufferSourceNode(AudioContextBase context)
        : base(context, inputCount: 0, outputCount: 1, "AudioBufferSource")
    {
        PlaybackRate = CreateAudioParam("playbackRate", 1f, 0.001f, 1000f, AutomationRate.KRate);
    }

    public void Start(double when = 0, double offset = 0, double duration = double.PositiveInfinity)
    {
        void Do(AudioContextBase _)
        {
            if (_hasStarted)
                throw new InvalidOperationException("AudioBufferSourceNode can only be started once.");

            if (_buffer is null)
                throw new InvalidOperationException("Cannot start without a buffer set");

            if (!_buffer.IsInitialized)
                throw new InvalidOperationException("Buffer is not initialized");

            _hasStarted = true;
            _startTime = Math.Max(0, when);
            _offset = Math.Max(0, offset);
            _duration = duration;
            _playbackPosition = (long)(_offset * _buffer.SampleRate);

            if (_resamplers is not null)
            {
                for (int i = 0; i < _resamplers.Length; i++)
                {
                    _resamplers[i].Clear();
                }
            }

            if (!double.IsPositiveInfinity(duration) && duration >= 0)
            {
                _stopTime = _startTime + duration;
                _hasStopped = true;
            }
        }

        Context.ExecuteOrPost(Do);
    }

    public void Stop(double when = 0)
    {
        void Do(AudioContextBase _)
        {
            if (_hasStopped)
                return;

            double at = Math.Max(0, when);
            _stopTime = double.IsNaN(_stopTime) ? at : Math.Min(_stopTime, at);
            _hasStopped = true;
        }

        Context.ExecuteOrPost(Do);
    }

    protected override void Process()
    {
        double t0 = Context.CurrentTime;
        double t1 = t0 + (double)AudioBuffer.FramesPerBlock / Context.SampleRate;
        bool shouldPlay = false;

        if (_hasStarted)
        {
            if (t1 > _startTime && (double.IsNaN(_stopTime) || t0 < _stopTime))
            {
                shouldPlay = true;
            }
        }

        if (!shouldPlay)
        {
            ProduceSilence();
            TryRaiseEndedEvent(t1);
            return;
        }

        if (_buffer is null || !_buffer.IsInitialized)
        {
            ProduceSilence();
            return;
        }

        int outputChannels = _buffer.NumberOfChannels;
        if (_outputBuffer is null || _outputBuffer.ChannelCount != outputChannels)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(outputChannels);
        }

        float playbackRate = PlaybackRate.GetValues()[0];
        int framesToRender = AudioBuffer.FramesPerBlock;

        double sampleRateRatio = _buffer.SampleRate / (double)Context.SampleRate;
        double effectiveRate = sampleRateRatio * playbackRate;

        long loopStartFrame = (long)(_loopStart * _buffer.SampleRate);
        long loopEndFrame = _loopEnd > 0
            ? (long)(_loopEnd * _buffer.SampleRate)
            : _buffer.Length;

        loopEndFrame = Math.Min(loopEndFrame, _buffer.Length);
        loopStartFrame = Math.Min(loopStartFrame, loopEndFrame);

        long durationEndFrame = _duration < double.PositiveInfinity
            ? (long)(_offset * _buffer.SampleRate) + (long)(_duration * _buffer.SampleRate)
            : _buffer.Length;
        durationEndFrame = Math.Min(durationEndFrame, _buffer.Length);

        bool hasMoreData = false;

        if (effectiveRate == 1.0)
        {
            for (int ch = 0; ch < outputChannels; ch++)
            {
                var channelData = _buffer.GetChannelData(ch);
                var outputSpan = _outputBuffer.GetChannelSpan(ch);
                long pos = _playbackPosition;
                int outIdx = 0;

                while (outIdx < framesToRender)
                {
                    if (_loop && pos >= loopEndFrame)
                    {
                        pos = loopStartFrame;
                    }

                    if (pos >= durationEndFrame && !_loop)
                    {
                        outputSpan.Slice(outIdx).Clear();
                        break;
                    }

                    long endFrame = _loop ? loopEndFrame : Math.Min(durationEndFrame, _buffer.Length);
                    int available = (int)Math.Min(endFrame - pos, framesToRender - outIdx);

                    if (available <= 0)
                    {
                        outputSpan.Slice(outIdx).Clear();
                        break;
                    }

                    channelData.Slice((int)pos, available).CopyTo(outputSpan.Slice(outIdx));
                    pos += available;
                    outIdx += available;
                    hasMoreData = true;
                }
            }

            _playbackPosition += framesToRender;

            if (_loop && _playbackPosition >= loopEndFrame)
            {
                long loopLength = loopEndFrame - loopStartFrame;
                if (loopLength > 0)
                {
                    long overshoot = _playbackPosition - loopEndFrame;
                    _playbackPosition = loopStartFrame + (overshoot % loopLength);
                }
            }
        }
        else
        {
            if (_resamplers is null || _resamplers.Length != outputChannels)
            {
                _resamplers = new CubicResampler[outputChannels];
                for (int i = 0; i < outputChannels; i++)
                {
                    _resamplers[i].Clear();
                }
            }

            if (_loopWrapBuffer is null)
            {
                _loopWrapBuffer = new float[512];
            }

            long totalInputConsumed = 0;
            for (int ch = 0; ch < outputChannels; ch++)
            {
                var channelData = _buffer.GetChannelData(ch);
                var outputSpan = _outputBuffer.GetChannelSpan(ch);
                long pos = _playbackPosition;
                long inputConsumedThisChannel = 0;

                ref var resampler = ref _resamplers[ch];
                int outIdx = 0;

                while (outIdx < framesToRender)
                {
                    if (_loop && pos >= loopEndFrame)
                    {
                        pos = loopStartFrame;
                    }

                    if (pos >= durationEndFrame && !_loop)
                    {
                        outputSpan.Slice(outIdx).Clear();
                        break;
                    }

                    long endFrame = _loop ? loopEndFrame : Math.Min(durationEndFrame, _buffer.Length);
                    int available = (int)Math.Min(endFrame - pos, _buffer.Length - pos);

                    if (available <= 0)
                    {
                        if (_loop)
                        {
                            pos = loopStartFrame;
                            inputConsumedThisChannel = pos - _playbackPosition;
                            continue;
                        }
                        else
                        {
                            outputSpan.Slice(outIdx).Clear();
                            break;
                        }
                    }

                    var outputSlice = outputSpan.Slice(outIdx);
                    ResampleResult result;

                    if (_loop && pos + available >= loopEndFrame - 4)
                    {
                        long loopLength = loopEndFrame - loopStartFrame;
                        int samplesFromEnd = (int)(loopEndFrame - pos);
                        int samplesNeeded = Math.Min(framesToRender - outIdx + 4, _loopWrapBuffer.Length);
                        int copied = 0;
                        for (int i = 0; i < samplesFromEnd && copied < samplesNeeded; i++)
                        {
                            _loopWrapBuffer[copied++] = channelData[(int)pos + i];
                        }

                        for (int i = 0; copied < samplesNeeded && i < loopLength; i++)
                        {
                            _loopWrapBuffer[copied++] = channelData[(int)loopStartFrame + i];
                        }

                        result = resampler.Process(_loopWrapBuffer.AsSpan(0, copied), outputSlice, effectiveRate);
                    }
                    else
                    {
                        result = resampler.Process(channelData.Slice((int)pos, available), outputSlice, effectiveRate);
                    }

                    if (result.OutputProduced > 0)
                        hasMoreData = true;

                    long newPos = pos + result.InputConsumed;
                    if (_loop && newPos >= loopEndFrame)
                    {
                        long overshoot = newPos - loopEndFrame;
                        newPos = loopStartFrame + overshoot;
                    }

                    inputConsumedThisChannel += (newPos >= pos) ? (newPos - pos) : (loopEndFrame - pos + newPos - loopStartFrame);
                    pos = newPos;
                    outIdx += result.OutputProduced;

                    if (result.InputConsumed == 0 && result.OutputProduced == 0)
                    {
                        outputSpan.Slice(outIdx).Clear();
                        break;
                    }
                }

                if (ch == 0)
                {
                    totalInputConsumed = inputConsumedThisChannel;
                }
            }

            _playbackPosition += totalInputConsumed;

            if (_loop && _playbackPosition >= loopEndFrame)
            {
                long loopLength = loopEndFrame - loopStartFrame;
                if (loopLength > 0)
                {
                    long overshoot = _playbackPosition - loopEndFrame;
                    _playbackPosition = loopStartFrame + (overshoot % loopLength);
                }
            }
        }

        if (!hasMoreData || (!_loop && _playbackPosition >= durationEndFrame))
        {
            _outputBuffer.Clear();
        }
        else
        {
            _outputBuffer.MarkAsNonSilent();
        }

        SetOutputBuffer(0, _outputBuffer);
    }

    private void TryRaiseEndedEvent(double currentTime)
    {
        if (_hasStarted && !double.IsNaN(_stopTime) && currentTime >= _stopTime)
        {
            if (!_endedEventRaised)
            {
                _endedEventRaised = true;
                Ended?.Invoke(this, EventArgs.Empty);
                Dispose();
            }
        }
    }

    private void ProduceSilence()
    {
        if (_outputBuffer is null || _outputBuffer.ChannelCount != 1)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(1);
        }

        _outputBuffer.Clear();
        SetOutputBuffer(0, _outputBuffer);
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }

        _buffer = null;
        base.OnDispose();
    }
}
