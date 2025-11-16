using System;
using System.Collections.Generic;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// Base class for all audio processing nodes in the graph.
/// </summary>
public abstract class AudioNode
{
    private static ulong _nextId = 0;

    public ulong NodeId { get; }
    public string Name { get; set; }

    private readonly List<AudioNodeInput> _inputs;
    private readonly List<AudioNodeOutput> _outputs;
    private readonly List<AudioParam> _params;

    private int _lastProcessedBlock = -1;
    private bool _isProcessing = false;
    private bool _disposed = false;

    public AudioContextBase Context { get; }
    internal IReadOnlyList<AudioParam> Params => _params;

    protected AudioNode(AudioContextBase context, int inputCount, int outputCount, string? name = null)
    {
        NodeId = System.Threading.Interlocked.Increment(ref _nextId);
        Context = context;
        Name = name ?? GetType().Name;

        _inputs = new List<AudioNodeInput>(inputCount);
        _outputs = new List<AudioNodeOutput>(outputCount);
        _params = new List<AudioParam>(4);

        for (int i = 0; i < inputCount; i++)
        {
            _inputs.Add(new AudioNodeInput(this, i, context.BufferPool));
        }

        for (int i = 0; i < outputCount; i++)
        {
            _outputs.Add(new AudioNodeOutput(this, i));
        }
    }

    public IReadOnlyList<AudioNodeInput> Inputs => _inputs;
    public IReadOnlyList<AudioNodeOutput> Outputs => _outputs;

    protected AudioParam CreateAudioParam(
        string name,
        float defaultValue,
        float minValue = float.MinValue,
        float maxValue = float.MaxValue,
        AutomationRate automationRate = AutomationRate.ARate)
    {
        var param = new AudioParam(this, name, defaultValue, minValue, maxValue, automationRate);
        _params.Add(param);
        return param;
    }

    /// <summary>
    /// Connect this node's output to another node's input.
    /// Returns the destination (target) node to enable chaining.
    /// </summary>
    public T Connect<T>(T destination, int outputIndex = 0, int inputIndex = 0)
        where T : AudioNode
    {
        ConnectInternal(destination, outputIndex, inputIndex);
        return destination;
    }

    /// <summary>
    /// Disconnect this node's output. If a destination is provided, disconnect only that connection.
    /// </summary>
    public void Disconnect(AudioNode? destination = null, int outputIndex = 0, int inputIndex = 0)
    {
        DisconnectInternal(destination, outputIndex, inputIndex);
    }

    /// <summary>
    /// Connect this node's output to an AudioParam.
    /// </summary>
    public void Connect(AudioParam param, int outputIndex = 0)
    {
        if (outputIndex < 0 || outputIndex >= Outputs.Count)
            throw new ArgumentOutOfRangeException(nameof(outputIndex));

        param.ConnectFrom(Outputs[outputIndex]);
    }

    /// <summary>
    /// Disconnect this node's output from an AudioParam.
    /// </summary>
    public void Disconnect(AudioParam param, int outputIndex = 0)
    {
        if (outputIndex < 0 || outputIndex >= Outputs.Count)
            throw new ArgumentOutOfRangeException(nameof(outputIndex));

        param.DisconnectFrom(Outputs[outputIndex]);
    }

    /// <summary>
    /// Internal method to connect this node's output to another node's input.
    /// Use the extension method for public API with type preservation.
    /// </summary>
    internal void ConnectInternal(AudioNode destination, int outputIndex = 0, int inputIndex = 0)
    {
        void DoConnect(AudioContextBase _)
        {
            if (outputIndex < 0 || outputIndex >= _outputs.Count)
                throw new ArgumentOutOfRangeException(nameof(outputIndex));

            if (inputIndex < 0 || inputIndex >= destination._inputs.Count)
                throw new ArgumentOutOfRangeException(nameof(inputIndex));

            _outputs[outputIndex].ConnectTo(destination._inputs[inputIndex]);
        }

        Context.ExecuteOrPost(DoConnect);
    }

    /// <summary>
    /// Internal method to disconnect this node's output from a specific destination.
    /// Use the extension method for public API with type preservation.
    /// </summary>
    internal void DisconnectInternal(AudioNode? destination = null, int outputIndex = 0, int inputIndex = 0)
    {
        void DoDisconnect(AudioContextBase _)
        {
            if (outputIndex < 0 || outputIndex >= _outputs.Count)
                throw new ArgumentOutOfRangeException(nameof(outputIndex));

            if (destination is null)
            {
                _outputs[outputIndex].DisconnectAll();
            }
            else
            {
                if (inputIndex < 0 || inputIndex >= destination._inputs.Count)
                    throw new ArgumentOutOfRangeException(nameof(inputIndex));

                _outputs[outputIndex].DisconnectFrom(destination._inputs[inputIndex]);
            }
        }

        Context.ExecuteOrPost(DoDisconnect);
    }

    internal void ProcessInternal(int blockNumber, double blockTime)
    {
        if (_lastProcessedBlock == blockNumber)
            return;

        if (_isProcessing)
        {
            throw new InvalidOperationException($"Audio graph cycle detected at node {Name} (ID: {NodeId})");
        }

        _isProcessing = true;
        _lastProcessedBlock = blockNumber;

        try
        {
            for (int i = 0; i < _params.Count; i++)
            {
                _params[i].ComputeValues(blockNumber, blockTime);
            }

            for (int i = 0; i < _inputs.Count; i++)
            {
                _inputs[i].Pull(blockNumber, blockTime);
            }

            Process();
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Override this to implement the node's audio processing logic.
    /// Read from Inputs, write to Outputs. All buffers are guaranteed to be 128 frames.
    /// </summary>
    protected abstract void Process();

    /// <summary>
    /// Sets the output buffer for a given output index. Called by nodes during Process().
    /// </summary>
    protected void SetOutputBuffer(int outputIndex, AudioBuffer buffer)
    {
        if (outputIndex < 0 || outputIndex >= _outputs.Count)
            throw new ArgumentOutOfRangeException(nameof(outputIndex));

        _outputs[outputIndex].SetBuffer(buffer);
    }

    /// <summary>
    /// Called when the node is disposed. Override to clean up resources.
    /// </summary>
    protected virtual void OnDispose() { }

    public void Dispose()
    {
        if (_disposed)
            return;

        void DoDispose(AudioContextBase _)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var output in _outputs)
            {
                output.DisconnectAll();
            }

            foreach (var input in _inputs)
            {
                input.DisconnectAll();
                input.Dispose();
            }

            foreach (var param in _params)
            {
                param.Dispose();
            }

            OnDispose();
        }

        Context.ExecuteOrPost(DoDispose);
    }
}
