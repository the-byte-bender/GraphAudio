using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A source node that outputs a constant value.
/// </summary>
/// <remarks>
/// This node automatically disposes itself when playback ends.
/// 
/// WARNING: Do NOT connect this node directly to the destination! Constant sources are meant for modulation and automation. Connecting to the destination will output a DC signal which can potentially damage some devices.
/// </remarks>
public sealed class ConstantSourceNode : AudioNode, IAudioScheduledSourceNode
{
    private AudioBuffer? _outputBuffer;
    private bool _hasStarted = false;
    private bool _hasStopped = false;
    private bool _endedRaised = false;
    private double _startTime = double.NaN;
    private double _stopTime = double.NaN;

    public event EventHandler? Ended;

    /// <summary>
    /// The constant offset value to output.
    /// </summary>
    public AudioParam Offset { get; }

    public ConstantSourceNode(AudioContextBase context)
        : base(context, inputCount: 0, outputCount: 1, "ConstantSource")
    {
        Offset = CreateAudioParam(
            name: "offset",
            defaultValue: 1f,
            minValue: float.MinValue,
            maxValue: float.MaxValue,
            automationRate: AutomationRate.ARate);
    }

    public void Start(double when = 0, double offset = 0, double duration = double.NaN)
    {
        void Do(AudioContextBase _)
        {
            if (_hasStarted)
                return;

            _hasStarted = true;
            _startTime = Math.Max(0, when);

            if (!double.IsNaN(duration) && duration >= 0)
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
        if (_outputBuffer is null)
        {
            _outputBuffer = Context.BufferPool.Rent(1);
        }

        double t0 = Context.CurrentTime;
        double t1 = t0 + (double)AudioBuffer.FramesPerBlock / Context.SampleRate;
        int startFrame = 0;
        int endFrame = AudioBuffer.FramesPerBlock;
        bool shouldPlay = false;

        if (_hasStarted)
        {
            if (t1 > _startTime && (double.IsNaN(_stopTime) || t0 < _stopTime))
            {
                shouldPlay = true;
                if (t0 < _startTime && _startTime < t1)
                {
                    startFrame = (int)Math.Clamp(
                        Math.Ceiling((_startTime - t0) * Context.SampleRate),
                        0,
                        AudioBuffer.FramesPerBlock);
                }

                if (!double.IsNaN(_stopTime) && t0 < _stopTime && _stopTime < t1)
                {
                    endFrame = (int)Math.Clamp(
                        Math.Floor((_stopTime - t0) * Context.SampleRate),
                        0,
                        AudioBuffer.FramesPerBlock);
                }
            }
        }

        if (!shouldPlay)
        {
            _outputBuffer.Clear();
            SetOutputBuffer(0, _outputBuffer);
            TryRaiseEndedAndDispose(t1);
            return;
        }

        var output = _outputBuffer.GetChannelSpan(0);
        var offsetValues = Offset.GetValues();

        if (startFrame > 0)
        {
            output[..startFrame].Fill(0f);
        }

        if (endFrame > startFrame)
        {
            offsetValues[startFrame..endFrame].CopyTo(output[startFrame..endFrame]);
        }

        if (endFrame < AudioBuffer.FramesPerBlock)
        {
            output[endFrame..].Fill(0f);
        }

        _outputBuffer.MarkAsNonSilent();
        SetOutputBuffer(0, _outputBuffer);

        TryRaiseEndedAndDispose(t1);
    }

    private void TryRaiseEndedAndDispose(double blockEndTime)
    {
        if (_hasStarted && _hasStopped && !_endedRaised && !double.IsNaN(_stopTime) && blockEndTime >= _stopTime)
        {
            _endedRaised = true;
            Ended?.Invoke(this, EventArgs.Empty);

            Dispose();
        }
    }

    protected override void OnDispose()
    {
        if (_outputBuffer is not null)
        {
            Context.BufferPool.Return(_outputBuffer);
            _outputBuffer = null;
        }
    }
}
