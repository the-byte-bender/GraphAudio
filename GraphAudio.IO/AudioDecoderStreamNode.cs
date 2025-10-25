using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using GraphAudio.Core;
using GraphAudio.IO;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that decodes audio on-demand from a file or any other stream.
/// 
/// This must be used in realtime contexts.
/// </summary>
public sealed class AudioDecoderStreamNode : AudioStreamNodeBase
{
    private readonly AudioDecoder _decoder;
    private readonly PlayableAudioBuffer[] _bufferPool;
    private readonly int _bufferSize;
    private readonly Thread _decoderThread;
    private readonly ConcurrentQueue<Action> _commandQueue;
    private readonly float[][] _decodeBuffer;
    private readonly float[][] _loopWrapBuffer;
    private volatile bool _disposed;
    private volatile bool _shouldDecode = true;
    private bool _loop;

    /// <summary>
    /// Whether the audio should loop.
    /// </summary>
    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    /// <summary>
    /// The duration of the audio in this stream.
    /// </summary>
    public TimeSpan Duration => _decoder.Duration;

    /// <summary>
    /// The sample rate of the audio in this stream.
    /// </summary>
    public int SampleRate => _decoder.SampleRate;

    private AudioDecoderStreamNode(AudioContextBase context, Stream stream, int bufferSize, int bufferCount)
        : base(context)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));
        if (bufferCount < 2)
            throw new ArgumentOutOfRangeException(nameof(bufferCount), "Must have at least 2 buffers");
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable", nameof(stream));

        _decoder = new AudioDecoder(stream);
        _bufferSize = bufferSize;
        _commandQueue = new ConcurrentQueue<Action>();
        _bufferPool = new PlayableAudioBuffer[bufferCount];
        for (int i = 0; i < bufferCount; i++)
        {
            _bufferPool[i] = new PlayableAudioBuffer(_decoder.Channels, bufferSize, _decoder.SampleRate);
            _bufferPool[i].MarkAsInitialized();
        }

        _decodeBuffer = new float[_decoder.Channels][];
        for (int ch = 0; ch < _decoder.Channels; ch++)
        {
            _decodeBuffer[ch] = new float[bufferSize];
        }

        _loopWrapBuffer = new float[_decoder.Channels][];
        for (int ch = 0; ch < _decoder.Channels; ch++)
        {
            _loopWrapBuffer[ch] = new float[bufferSize];
        }

        _decoderThread = new Thread(DecoderThreadLoop)
        {
            IsBackground = true,
            Name = "AudioDecoderThread",
            Priority = ThreadPriority.AboveNormal
        };
        _decoderThread.Start();
        _commandQueue.Enqueue(RefillAllBuffers);
    }

    /// <summary>
    /// Creates a new AudioDecoderStreamNode from a file path.
    /// </summary>
    public static AudioDecoderStreamNode FromFile(AudioContextBase context, string filePath, int bufferSize = 4096, int bufferCount = 3)
    {
        var stream = File.OpenRead(filePath);
        return new AudioDecoderStreamNode(context, stream, bufferSize, bufferCount);
    }

    /// <summary>
    /// Creates a new AudioDecoderStreamNode from a file path.
    /// </summary>
    public static Task<AudioDecoderStreamNode> FromFileAsync(AudioContextBase context, string filePath, int bufferSize = 4096, int bufferCount = 3)
    {
        return Task.Run(() =>
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false);
            return new AudioDecoderStreamNode(context, stream, bufferSize, bufferCount);
        });
    }

    /// <summary>
    /// Creates a new AudioDecoderStreamNode from a stream.
    /// Takes ownership of the stream: it will be disposed when the node is disposed.
    /// </summary>
    public static AudioDecoderStreamNode FromStream(AudioContextBase context, Stream stream, int bufferSize = 4096, int bufferCount = 3)
    {
        return new AudioDecoderStreamNode(context, stream, bufferSize, bufferCount);
    }

    /// <summary>
    /// Seeks to a specific position in the audio.
    /// </summary>
    public void Seek(TimeSpan position)
    {
        var wasPlaying = State == StreamState.Playing;
        Stop();

        _commandQueue.Enqueue(() =>
        {
            if (!_decoder.TrySeek(position))
            {
                _decoder.TryRewind();
            }

            RefillAllBuffers();
        });

        if (wasPlaying)
        {
            State = StreamState.Playing;
        }
    }

    ///  <inheritdoc/>
    public override void Play()
    {
        if (State == StreamState.Stopped)
        {
            _commandQueue.Enqueue(() =>
            {
                _decoder.TryRewind();
                RefillAllBuffers();
            });
        }
        base.Play();
    }

    private void DecoderThreadLoop()
    {
        while (!_disposed)
        {
            while (_commandQueue.TryDequeue(out var command))
            {
                if (_disposed) break;
                command();
            }

            if (!_disposed && _shouldDecode && State != StreamState.Stopped)
            {
                if (TryDequeueProcessedBuffer(out var processedBuffer) && processedBuffer is not null)
                {
                    FillBuffer(processedBuffer);
                    QueueBuffer(processedBuffer);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private void FillBuffer(PlayableAudioBuffer buffer)
    {
        long framesRead = _decoder.DecodePlanar(_decodeBuffer);

        if (framesRead < _bufferSize)
        {
            if (_loop)
            {
                _decoder.TryRewind();

                if (framesRead < _bufferSize)
                {
                    long remaining = _bufferSize - framesRead;
                    int offset = (int)framesRead;
                    long additionalFrames = _decoder.DecodePlanar(_loopWrapBuffer);

                    for (int ch = 0; ch < _decoder.Channels; ch++)
                    {
                        Array.Copy(_loopWrapBuffer[ch], 0, _decodeBuffer[ch], offset, (int)Math.Min(additionalFrames, remaining));
                    }

                    framesRead += Math.Min(additionalFrames, remaining);
                }
            }
            else
            {
                for (int ch = 0; ch < _decoder.Channels; ch++)
                {
                    Array.Clear(_decodeBuffer[ch], (int)framesRead, _bufferSize - (int)framesRead);
                }
            }
        }

        for (int ch = 0; ch < _decoder.Channels; ch++)
        {
            buffer.CopyToChannel(_decodeBuffer[ch], ch);
        }
    }

    private void RefillAllBuffers()
    {
        while (TryDequeueProcessedBuffer(out _)) { }

        foreach (var buffer in _bufferPool)
        {
            FillBuffer(buffer);
            QueueBuffer(buffer);
        }
    }

    protected override void OnDispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _shouldDecode = false;

            if (_decoderThread.IsAlive)
            {
                _decoderThread.Join(1000);
            }
            _decoder.Dispose();
        }

        base.OnDispose();
    }
}
