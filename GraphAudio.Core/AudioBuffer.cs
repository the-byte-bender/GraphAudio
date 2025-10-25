using System;

namespace GraphAudio.Core;

/// <summary>
/// Represents a single or multi-channel audio buffer with a fixed frame size.
/// </summary>
public sealed class AudioBuffer
{
    public const int FramesPerBlock = 128;

    private readonly float[][] _channels;
    private readonly int _channelCount;
    private bool _isSilent;

    public AudioBuffer(int channelCount)
    {
        if (channelCount < 1 || channelCount > 32)
            throw new ArgumentOutOfRangeException(nameof(channelCount));

        _channelCount = channelCount;
        _channels = new float[channelCount][];

        for (int i = 0; i < channelCount; i++)
        {
            _channels[i] = new float[FramesPerBlock];
        }

        _isSilent = true;
    }

    public int ChannelCount => _channelCount;
    public bool IsSilent => _isSilent;

    /// <summary>
    /// Get a span to the channel's sample data.
    /// </summary>
    public Span<float> GetChannelSpan(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));

        return _channels[channelIndex].AsSpan();
    }

    /// <summary>
    /// Get read-only access to the channel array. Use for direct array access.
    /// </summary>
    public float[] GetChannelData(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _channelCount)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));

        return _channels[channelIndex];
    }

    /// <summary>
    /// Clear all channels to silence (zeros).
    /// </summary>
    public void Clear()
    {
        for (int ch = 0; ch < _channelCount; ch++)
        {
            Array.Clear(_channels[ch], 0, FramesPerBlock);
        }
        _isSilent = true;
    }

    /// <summary>
    /// Mark this buffer as containing audio (not silent).
    /// Call this after writing data to the buffer.
    /// </summary>
    public void MarkAsNonSilent()
    {
        _isSilent = false;
    }

    /// <summary>
    /// Copy data from another buffer, handling channel count differences.
    /// </summary>
    public void CopyFrom(AudioBuffer source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (source._isSilent)
        {
            Clear();
            return;
        }

        int minChannels = Math.Min(_channelCount, source._channelCount);

        for (int ch = 0; ch < minChannels; ch++)
        {
            source._channels[ch].AsSpan().CopyTo(_channels[ch].AsSpan());
        }

        for (int ch = minChannels; ch < _channelCount; ch++)
        {
            Array.Clear(_channels[ch], 0, FramesPerBlock);
        }

        _isSilent = false;
    }

    /// <summary>
    /// Copy a single channel from source to a destination channel.
    /// </summary>
    public void CopyChannelFrom(AudioBuffer source, int sourceChannel, int destChannel)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (sourceChannel < 0 || sourceChannel >= source._channelCount)
            throw new ArgumentOutOfRangeException(nameof(sourceChannel));
        if (destChannel < 0 || destChannel >= _channelCount)
            throw new ArgumentOutOfRangeException(nameof(destChannel));

        source._channels[sourceChannel].AsSpan().CopyTo(_channels[destChannel].AsSpan());
        _isSilent = false;
    }

    /// <summary>
    /// Fill all channels with a constant value (useful for testing).
    /// </summary>
    public void Fill(float value)
    {
        for (int ch = 0; ch < _channelCount; ch++)
        {
            _channels[ch].AsSpan().Fill(value);
        }
        _isSilent = (value == 0f);
    }

    /// <summary>
    /// Apply gain to all channels.
    /// </summary>
    public void ApplyGain(float gain)
    {
        if (gain == 1.0f || _isSilent)
            return;

        if (gain == 0f)
        {
            Clear();
            return;
        }

        for (int ch = 0; ch < _channelCount; ch++)
        {
            var span = _channels[ch].AsSpan();
            for (int i = 0; i < FramesPerBlock; i++)
            {
                span[i] *= gain;
            }
        }
    }

    /// <summary>
    /// Check if the buffer actually contains silence (all zeros).
    /// Updates the IsSilent flag.
    /// </summary>
    public bool DetectSilence(float threshold = 0.0f)
    {
        for (int ch = 0; ch < _channelCount; ch++)
        {
            var span = _channels[ch].AsSpan();
            for (int i = 0; i < FramesPerBlock; i++)
            {
                if (Math.Abs(span[i]) > threshold)
                {
                    _isSilent = false;
                    return false;
                }
            }
        }

        _isSilent = true;
        return true;
    }
}
