using System;
using System.Collections.Concurrent;
using System.Threading;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Nodes;

/// <summary>
/// State of the audio stream.
/// </summary>
public enum StreamState
{
    Playing,
    Paused,
    Stopped
}

public abstract class AudioStreamNodeBase : AudioNode
{
    private readonly ConcurrentQueue<PlayableAudioBuffer> _queuedBuffers;
    private readonly ConcurrentQueue<PlayableAudioBuffer> _processedBuffers;
    private PlayableAudioBuffer? _currentBuffer;
    private long _currentBufferPosition;
    private int _lastBufferSampleRate;
    private AudioBuffer? _outputBuffer;
    private CubicResampler[]? _resamplers;
    private int _state;

    public AudioParam PlaybackRate { get; }

    /// <summary>
    /// The current state of the stream.
    /// </summary>
    public StreamState State
    {
        get => (StreamState)Interlocked.CompareExchange(ref _state, 0, 0);
        protected set
        {
            var newState = (int)value;
            var oldState = Interlocked.Exchange(ref _state, newState);

            if (value == StreamState.Stopped && oldState != (int)StreamState.Stopped)
            {
                FlushToProcessed();
            }
        }
    }

    /// <summary>
    /// Gets the number of buffers currently queued and ready to play.
    /// </summary>
    protected int QueuedBufferCount => _queuedBuffers.Count;

    /// <summary>
    /// Gets the number of buffers that have been processed and are available for refilling.
    /// </summary>
    protected int ProcessedBufferCount => _processedBuffers.Count;

    protected AudioStreamNodeBase(AudioContextBase context)
        : base(context, inputCount: 0, outputCount: 1, "AudioStreamSource")
    {
        _queuedBuffers = new ConcurrentQueue<PlayableAudioBuffer>();
        _processedBuffers = new ConcurrentQueue<PlayableAudioBuffer>();
        _state = (int)StreamState.Stopped;

        PlaybackRate = CreateAudioParam("playbackRate", 1f, 0.001f, 1000f, AutomationRate.KRate);
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public virtual void Play()
    {
        State = StreamState.Playing;
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public virtual void Pause()
    {
        State = StreamState.Paused;
    }

    /// <summary>
    /// Stops playback.
    /// </summary>
    public virtual void Stop()
    {
        State = StreamState.Stopped;
    }

    private void FlushToProcessed()
    {
        var current = Interlocked.Exchange(ref _currentBuffer, null);
        if (current is not null)
        {
            _processedBuffers.Enqueue(current);
        }

        while (_queuedBuffers.TryDequeue(out var buffer))
        {
            _processedBuffers.Enqueue(buffer);
        }

        if (_resamplers is not null)
        {
            for (int i = 0; i < _resamplers.Length; i++)
            {
                _resamplers[i].Clear();
            }
        }

        _currentBufferPosition = 0;
        _lastBufferSampleRate = 0;
    }

    protected void QueueBuffer(PlayableAudioBuffer buffer)
    {
        if (!buffer.IsInitialized)
            throw new ArgumentException("Buffer must be initialized", nameof(buffer));

        _queuedBuffers.Enqueue(buffer);
    }

    protected bool TryDequeueProcessedBuffer(out PlayableAudioBuffer? buffer)
    {
        return _processedBuffers.TryDequeue(out buffer);
    }

    protected override sealed void Process()
    {
        var state = State;

        if (state != StreamState.Playing)
        {
            ProduceSilence();
            return;
        }

        if (_currentBuffer is null)
        {
            if (!_queuedBuffers.TryDequeue(out _currentBuffer))
            {
                ProduceSilence();
                return;
            }
            _currentBufferPosition = 0;
        }

        int channelCount = _currentBuffer.NumberOfChannels;

        if (_outputBuffer is null || _outputBuffer.ChannelCount != channelCount)
        {
            if (_outputBuffer is not null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(channelCount);
        }

        int framesToRender = AudioBuffer.FramesPerBlock;
        int framesRendered = 0;

        if (_resamplers is null || _resamplers.Length != channelCount)
        {
            _resamplers = new CubicResampler[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _resamplers[i].Clear();
            }
        }

        while (framesRendered < framesToRender)
        {
            if (_currentBuffer is null)
            {
                if (!_queuedBuffers.TryDequeue(out _currentBuffer))
                {
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        _outputBuffer.GetChannelSpan(ch).Slice(framesRendered).Clear();
                    }
                    break;
                }
                _currentBufferPosition = 0;

                int newChannelCount = _currentBuffer.NumberOfChannels;
                if (newChannelCount != channelCount)
                {
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        _outputBuffer.GetChannelSpan(ch).Slice(framesRendered).Clear();
                    }
                    _queuedBuffers.Enqueue(_currentBuffer);
                    _currentBuffer = null;
                    break;
                }
            }

            int bufferSampleRate = _currentBuffer.SampleRate;

            if (bufferSampleRate != _lastBufferSampleRate && _lastBufferSampleRate != 0 && _resamplers is not null)
            {
                for (int i = 0; i < _resamplers.Length; i++)
                {
                    _resamplers[i].Clear();
                }
            }
            _lastBufferSampleRate = bufferSampleRate;

            float playbackRate = PlaybackRate.GetValues()[0];
            double sampleRateRatio = bufferSampleRate / (double)Context.SampleRate;
            double effectiveRate = sampleRateRatio * playbackRate;

            if (effectiveRate == 1.0)
            {
                int remainingInBuffer = _currentBuffer.Length - (int)_currentBufferPosition;
                int remainingInOutput = framesToRender - framesRendered;
                int framesToCopy = Math.Min(remainingInBuffer, remainingInOutput);

                for (int ch = 0; ch < channelCount; ch++)
                {
                    var sourceData = _currentBuffer.GetChannelData(ch);
                    var destSpan = _outputBuffer.GetChannelSpan(ch);
                    sourceData.Slice((int)_currentBufferPosition, framesToCopy)
                        .CopyTo(destSpan.Slice(framesRendered));
                }

                _currentBufferPosition += framesToCopy;
                framesRendered += framesToCopy;

                if (_currentBufferPosition >= _currentBuffer.Length)
                {
                    _processedBuffers.Enqueue(_currentBuffer);
                    _currentBuffer = null;
                    _currentBufferPosition = 0;
                }
            }
            else
            {
                long minInputConsumed = long.MaxValue;
                int outputProduced = 0;

                for (int ch = 0; ch < channelCount; ch++)
                {
                    var sourceData = _currentBuffer.GetChannelData(ch);
                    var destSpan = _outputBuffer.GetChannelSpan(ch);

                    int available = _currentBuffer.Length - (int)_currentBufferPosition;
                    if (available <= 0)
                        break;

                    var result = _resamplers![ch].Process(
                        sourceData.Slice((int)_currentBufferPosition, available),
                        destSpan.Slice(framesRendered, framesToRender - framesRendered),
                        effectiveRate
                    );

                    if (ch == 0)
                    {
                        minInputConsumed = result.InputConsumed;
                        outputProduced = result.OutputProduced;
                    }
                    else
                    {
                        minInputConsumed = Math.Min(minInputConsumed, result.InputConsumed);
                    }
                }

                _currentBufferPosition += minInputConsumed;
                framesRendered += outputProduced;

                if (_currentBufferPosition >= _currentBuffer.Length - 4)
                {
                    _processedBuffers.Enqueue(_currentBuffer);
                    _currentBuffer = null;
                    _currentBufferPosition = 0;
                }

                if (minInputConsumed == 0)
                {
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        _outputBuffer.GetChannelSpan(ch).Slice(framesRendered).Clear();
                    }
                    break;
                }
            }
        }

        if (framesRendered > 0)
        {
            _outputBuffer.MarkAsNonSilent();
        }
        else
        {
            _outputBuffer.Clear();
        }

        SetOutputBuffer(0, _outputBuffer);
    }

    private void ProduceSilence()
    {
        if (_outputBuffer is null || _outputBuffer.ChannelCount != 1)
        {
            if (_outputBuffer is null)
                Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = Context.BufferPool.Rent(1);
        }

        _outputBuffer.Clear();
        SetOutputBuffer(0, _outputBuffer);
    }

    protected override void OnDispose()
    {
        if (State != StreamState.Stopped)
        {
            Stop();
        }
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }
        base.OnDispose();
    }
}
