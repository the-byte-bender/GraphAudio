using System;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// A sound that streams audio in real-time.
/// </summary>
public sealed class StreamingSound : Sound
{
    private readonly AudioDecoderStreamNode _streamNode;

    /// <inheritdoc/>
    public override bool IsPlaying => _streamNode.State == StreamState.Playing;

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => _streamNode.Loop;
        set => _streamNode.Loop = value;
    }

    /// <inheritdoc/>
    public override float PlaybackRate
    {
        get => _streamNode.PlaybackRate.Value;
        set => _streamNode.PlaybackRate.Value = value;
    }

    /// <inheritdoc/>
    public override TimeSpan Duration => _streamNode.Duration;

    /// <summary>
    /// The sample rate of the audio stream.
    /// </summary>
    public int SampleRate => _streamNode.SampleRate;

    internal StreamingSound(AudioEngine engine, AudioDecoderStreamNode streamNode, SoundMixState mixState, AudioBus? bus = null)
        : base(engine, mixState, bus)
    {
        _streamNode = streamNode;

        _streamNode.Connect(Input);
    }

    /// <inheritdoc/>
    public override void Seek(TimeSpan position)
    {
        _streamNode.Seek(position);
    }

    protected override void DoPlay()
    {
        _streamNode.Play();
    }

    protected override void DoPause()
    {
        _streamNode.Pause();
    }

    protected override void DoStop()
    {
        _streamNode.Stop();
    }

    protected override void OnDispose()
    {
        _streamNode?.Dispose();
        base.OnDispose();
    }
}
