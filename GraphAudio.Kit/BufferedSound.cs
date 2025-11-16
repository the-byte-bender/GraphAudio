using System;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// A sound that plays from a pre-loaded audio buffer.
/// </summary>
public sealed class BufferedSound : Sound
{
    private readonly PlayableAudioBuffer _buffer;
    private AudioBufferSourceNode? _sourceNode;
    private bool _isDisposed;
    private double _currentOffset;

    private bool _loop;
    private double _loopStart;
    private double _loopEnd;
    private float _playbackRate = 1.0f;

    /// <inheritdoc/>
    public override bool IsPlaying => _sourceNode is not null && !_isDisposed;

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_sourceNode is not null)
                _sourceNode.Loop = value;
        }
    }

    /// <inheritdoc/>
    public override float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value;
            if (_sourceNode is not null)
                _sourceNode.PlaybackRate.Value = value;
        }
    }

    /// <inheritdoc/>
    public override TimeSpan Duration => TimeSpan.FromSeconds(_buffer.Duration);

    /// <summary>
    /// The loop start point in seconds.
    /// </summary>
    public double LoopStart
    {
        get => _loopStart;
        set
        {
            _loopStart = value;
            if (_sourceNode is not null)
                _sourceNode.LoopStart = value;
        }
    }

    /// <summary>
    /// The loop end point in seconds (0 means end of buffer).
    /// </summary>
    public double LoopEnd
    {
        get => _loopEnd;
        set
        {
            _loopEnd = value;
            if (_sourceNode is not null)
                _sourceNode.LoopEnd = value;
        }
    }

    internal BufferedSound(AudioEngine engine, PlayableAudioBuffer buffer, SoundMixState mixState, AudioBus? bus = null)
        : base(engine, mixState, bus)
    {
        _buffer = buffer;
    }

    private void CreateSourceNode()
    {
        _sourceNode = new AudioBufferSourceNode(Engine.Context)
        {
            Buffer = _buffer,
            Loop = _loop,
            LoopStart = _loopStart,
            LoopEnd = _loopEnd
        };
        _sourceNode.PlaybackRate.Value = _playbackRate;
        _sourceNode.Connect(Input);
        _sourceNode.Ended += OnSourceEnded;
    }

    private void OnSourceEnded(object? sender, EventArgs e)
    {
        DisposeSourceNode();
    }

    private void DisposeSourceNode()
    {
        if (_sourceNode is not null)
        {
            _sourceNode.Ended -= OnSourceEnded;
            _sourceNode.Dispose();
            _sourceNode = null;
        }
    }

    /// <inheritdoc/>
    public override void Seek(TimeSpan position)
    {
        double positionSeconds = Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds);
        _currentOffset = positionSeconds;

        if (_sourceNode is not null)
        {
            DisposeSourceNode();
            CreateSourceNode();
            _sourceNode!.Start(0, _currentOffset);
        }
    }

    protected override void DoPlay()
    {
        if (_sourceNode is null)
        {
            CreateSourceNode();
            _sourceNode!.Start(0, _currentOffset, double.PositiveInfinity);
        }
    }

    protected override void DoPause()
    {
        DoStop();
    }

    protected override void DoStop()
    {
        if (_sourceNode is not null)
        {
            _sourceNode.Stop(0);
            DisposeSourceNode();
        }
    }

    protected override void OnDispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            DisposeSourceNode();
        }
        base.OnDispose();
    }
}
