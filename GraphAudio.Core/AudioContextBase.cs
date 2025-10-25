using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using GraphAudio.Nodes;

namespace GraphAudio.Core;

/// <summary>
/// Abstract base class for audio contexts. 
/// </summary>
public abstract class AudioContextBase : IDisposable
{
    private readonly ConcurrentQueue<Action<AudioContextBase>> _pendingCommands = new();
    private int _currentBlock = 0;
    private double _currentTime = 0.0;
    private bool _disposed = false;
    private int _renderThreadId = -1;
    private int _inRender = 0;

    public int SampleRate { get; }
    public BufferPool BufferPool { get; }

    /// <summary>
    /// The current time in seconds.
    /// </summary>
    public double CurrentTime => Volatile.Read(ref _currentTime);

    /// <summary>
    /// The final output of the graph.
    /// </summary>
    public AudioDestinationNode Destination { get; }

    protected AudioContextBase(int sampleRate = 48000)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        SampleRate = sampleRate;
        BufferPool = new BufferPool();
        Destination = new AudioDestinationNode(this);

        BufferPool.Prewarm(1, 32);
        BufferPool.Prewarm(2, 32);
    }

    /// <summary>
    /// Process one block (128 frames) of audio.
    /// Returns the output buffer from the destination node.
    /// </summary>
    public AudioBuffer ProcessBlock()
    {
        if (Volatile.Read(ref _disposed))
            throw new ObjectDisposedException(GetType().Name);

        DrainCommands();

        if (Volatile.Read(ref _renderThreadId) == -1)
        {
            Volatile.Write(ref _renderThreadId, Environment.CurrentManagedThreadId);
        }

        int nextBlock = _currentBlock + 1;
        Volatile.Write(ref _currentBlock, nextBlock);

        var blockTime = _currentTime;
        try
        {
            Volatile.Write(ref _inRender, 1);
            Destination.ProcessInternal(nextBlock, blockTime);
        }
        finally
        {
            Volatile.Write(ref _inRender, 0);
        }

        var increment = (double)AudioBuffer.FramesPerBlock / SampleRate;
        Volatile.Write(ref _currentTime, blockTime + increment);
        return Destination.GetOutputBuffer();
    }

    /// <summary>
    /// Process one block and write the interleaved result into a provided float buffer.
    /// The buffer length must be AudioBuffer.FramesPerBlock * channels.
    /// This avoids creating an intermediate AudioBuffer when the consumer needs interleaved data.
    /// </summary>
    public unsafe void ProcessBlockInterleaved(float[] interleavedBuffer, int channels)
    {
        if (Volatile.Read(ref _disposed))
            throw new ObjectDisposedException(GetType().Name);

        if (channels < 1 || channels > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (interleavedBuffer.Length < AudioBuffer.FramesPerBlock * channels)
            throw new ArgumentException("Buffer too small for interleaved output.", nameof(interleavedBuffer));

        DrainCommands();

        if (Volatile.Read(ref _renderThreadId) == -1)
        {
            Volatile.Write(ref _renderThreadId, Environment.CurrentManagedThreadId);
        }

        int nextBlock = _currentBlock + 1;
        Volatile.Write(ref _currentBlock, nextBlock);

        var blockTime = _currentTime;
        try
        {
            Volatile.Write(ref _inRender, 1);
            Destination.ProcessInternal(nextBlock, blockTime);
        }
        finally
        {
            Volatile.Write(ref _inRender, 0);
        }

        var increment = (double)AudioBuffer.FramesPerBlock / SampleRate;
        Volatile.Write(ref _currentTime, blockTime + increment);

        var buffer = Destination.GetOutputBuffer();
        if (buffer is null)
        {
            Array.Clear(interleavedBuffer, 0, AudioBuffer.FramesPerBlock * channels);
            return;
        }

        int usedChannels = Math.Min(channels, buffer.ChannelCount);
        int frames = AudioBuffer.FramesPerBlock;

        fixed (float* outPtr = interleavedBuffer)
        {
            for (int ch = 0; ch < usedChannels; ch++)
            {
                var channelData = buffer.GetChannelData(ch);
                fixed (float* chPtr = channelData)
                {
                    float* src = chPtr;
                    float* dst = outPtr + ch;
                    for (int f = 0; f < frames; f++)
                    {
                        *dst = *src++;
                        dst += channels;
                    }
                }
            }

            if (usedChannels < channels)
            {
                for (int ch = usedChannels; ch < channels; ch++)
                {
                    float* dst = outPtr + ch;
                    for (int f = 0; f < frames; f++)
                    {
                        *dst = 0f;
                        dst += channels;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Process multiple blocks at once. More efficient for batch processing.
    /// </summary>
    public void ProcessBlocks(float[][] outputBuffers, int blockCount)
    {
        if (blockCount < 0)
            throw new ArgumentOutOfRangeException(nameof(blockCount));

        for (int block = 0; block < blockCount; block++)
        {
            var buffer = ProcessBlock();

            int channels = Math.Min(outputBuffers.Length, buffer.ChannelCount);
            for (int ch = 0; ch < channels; ch++)
            {
                if (outputBuffers[ch] != null)
                {
                    var src = buffer.GetChannelSpan(ch);
                    var dst = outputBuffers[ch].AsSpan(block * AudioBuffer.FramesPerBlock, AudioBuffer.FramesPerBlock);
                    src.CopyTo(dst);
                }
            }
        }
    }

    /// <summary>
    /// Get all nodes in the context (useful for debugging/visualization).
    /// </summary>
    public IReadOnlyList<AudioNode> GetAllNodes()
    {
        var list = new List<AudioNode>(32);
        var visited = new HashSet<ulong>();
        var stack = new Stack<AudioNode>();
        stack.Push(Destination);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node.NodeId)) continue;
            list.Add(node);

            var inputs = node.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var outputs = input.ConnectedOutputs;
                for (int j = 0; j < outputs.Count; j++)
                {
                    var upstream = outputs[j].Owner;
                    if (!visited.Contains(upstream.NodeId))
                        stack.Push(upstream);
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Get the current block number (useful for debugging).
    /// </summary>
    public int CurrentBlock => Volatile.Read(ref _currentBlock);

    /// <summary>
    /// Convert sample frames to time in seconds.
    /// </summary>
    public double FramesToSeconds(long frames)
    {
        return (double)frames / SampleRate;
    }

    /// <summary>
    /// Convert time in seconds to sample frames.
    /// </summary>
    public long SecondsToFrames(double seconds)
    {
        return (long)(seconds * SampleRate);
    }

    public event Action? Disposing;

    protected virtual void Dispose(bool disposing)
    {
        if (Volatile.Read(ref _disposed)) return;

        if (disposing)
        {
            Disposing?.Invoke();
            Destination.Dispose();
        }

        Volatile.Write(ref _disposed, true);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Enqueue a command to be executed on the render thread at the start of the next ProcessBlock.
    /// The action executes on the context thread and must not block.
    /// </summary>
    public void Post(Action<AudioContextBase> command)
    {
        if (Volatile.Read(ref _disposed)) throw new ObjectDisposedException(GetType().Name);
        _pendingCommands.Enqueue(command);
    }

    private void DrainCommands()
    {
        while (_pendingCommands.TryDequeue(out var cmd))
        {
            try
            {
                cmd(this);
            }
            catch
            {
            }
        }
    }

    internal bool IsRenderThread => Volatile.Read(ref _renderThreadId) == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Execute immediately if on the render thread and not mid-render, otherwise enqueue for the next block.
    /// </summary>
    internal void ExecuteOrPost(Action<AudioContextBase> command)
    {
        if (Volatile.Read(ref _disposed)) throw new ObjectDisposedException(GetType().Name);

        bool onRender = IsRenderThread;
        bool inRender = Volatile.Read(ref _inRender) == 1;
        if (onRender && !inRender)
        {
            command(this);
        }
        else
        {
            _pendingCommands.Enqueue(command);
        }
    }
}
