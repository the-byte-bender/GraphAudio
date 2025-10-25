using System;
using System.Threading;

namespace GraphAudio.Core;

/// <summary>
/// Represents an audio buffer that can be loaded with audio data and played back.
/// </summary>
public sealed class PlayableAudioBuffer
{
    private readonly float[][] _channels;
    private readonly int _sampleRate;
    private readonly int _length;
    private readonly int _numberOfChannels;
    private volatile bool _isInitialized;

    /// <summary>
    /// Number of audio channels.
    /// </summary>
    public int NumberOfChannels => _numberOfChannels;

    /// <summary>
    /// Length of the buffer in sample frames.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Sample rate in Hz.
    /// </summary>
    public int SampleRate => _sampleRate;

    /// <summary>
    /// Duration of the buffer in seconds.
    /// </summary>
    public double Duration => _length / (double)_sampleRate;

    /// <summary>
    /// Gets whether the buffer has been initialized with data.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Creates a new PlayableAudioBuffer with the specified configuration.
    /// </summary>
    public PlayableAudioBuffer(int numberOfChannels, int length, int sampleRate)
    {
        if (numberOfChannels < 1 || numberOfChannels > 32)
            throw new ArgumentOutOfRangeException(nameof(numberOfChannels), "Channel count must be between 1 and 32");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative");

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive");

        _numberOfChannels = numberOfChannels;
        _length = length;
        _sampleRate = sampleRate;
        _channels = new float[numberOfChannels][];

        for (int i = 0; i < numberOfChannels; i++)
        {
            _channels[i] = new float[length];
        }

        _isInitialized = false;
    }

    /// <summary>
    /// Gets a read-only span of the channel data.
    /// </summary>
    public ReadOnlySpan<float> GetChannelData(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _numberOfChannels)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));

        Thread.MemoryBarrier();
        return _channels[channelIndex].AsSpan();
    }

    /// <summary>
    /// Copies data into a specific channel. Should only be called during initialization.
    /// </summary>
    public void CopyToChannel(ReadOnlySpan<float> source, int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _numberOfChannels)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));

        if (source.Length > _length)
            throw new ArgumentException("Source data is larger than buffer length", nameof(source));

        source.CopyTo(_channels[channelIndex].AsSpan());
    }

    /// <summary>
    /// Copies data from a specific channel.
    /// </summary>
    public void CopyFromChannel(Span<float> destination, int channelIndex, int startFrame = 0)
    {
        if (channelIndex < 0 || channelIndex >= _numberOfChannels)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));

        if (startFrame < 0 || startFrame >= _length)
            throw new ArgumentOutOfRangeException(nameof(startFrame));

        int framesToCopy = Math.Min(destination.Length, _length - startFrame);
        _channels[channelIndex].AsSpan(startFrame, framesToCopy).CopyTo(destination);
    }

    /// <summary>
    /// Marks the buffer as initialized. Should be called after all data has been loaded.
    /// </summary>
    public void MarkAsInitialized()
    {
        Thread.MemoryBarrier();
        _isInitialized = true;
    }

    /// <summary>
    /// Creates a PlayableAudioBuffer from multi-channel float arrays.
    /// </summary>
    public static PlayableAudioBuffer FromChannelArrays(float[][] channelData, int sampleRate)
    {
        if (channelData.Length == 0)
            throw new ArgumentException("Channel data cannot be or empty", nameof(channelData));

        int numberOfChannels = channelData.Length;
        int length = channelData[0].Length;

        for (int i = 1; i < numberOfChannels; i++)
        {
            if (channelData[i].Length != length)
                throw new ArgumentException("All channels must have the same length", nameof(channelData));
        }

        var buffer = new PlayableAudioBuffer(numberOfChannels, length, sampleRate);

        for (int ch = 0; ch < numberOfChannels; ch++)
        {
            buffer.CopyToChannel(channelData[ch], ch);
        }

        buffer.MarkAsInitialized();
        return buffer;
    }

    /// <summary>
    /// Creates a mono PlayableAudioBuffer from a single float array.
    /// </summary>
    public static PlayableAudioBuffer FromMonoArray(float[] audioData, int sampleRate)
    {
        if (audioData == null)
            throw new ArgumentNullException(nameof(audioData));

        var buffer = new PlayableAudioBuffer(1, audioData.Length, sampleRate);
        buffer.CopyToChannel(audioData, 0);
        buffer.MarkAsInitialized();
        return buffer;
    }

    /// <summary>
    /// Creates a stereo PlayableAudioBuffer from left and right channel arrays.
    /// </summary>
    public static PlayableAudioBuffer FromStereoArrays(float[] leftChannel, float[] rightChannel, int sampleRate)
    {
        if (leftChannel.Length != rightChannel.Length)
            throw new ArgumentException("Left and right channels must have the same length");

        var buffer = new PlayableAudioBuffer(2, leftChannel.Length, sampleRate);
        buffer.CopyToChannel(leftChannel, 0);
        buffer.CopyToChannel(rightChannel, 1);
        buffer.MarkAsInitialized();
        return buffer;
    }
}
