using System;
using GraphAudio.Core;

namespace GraphAudio.Nodes;

/// <summary>
/// A node that generates periodic waveforms (sine, square, sawtooth, triangle).
/// </summary>
/// <remarks>
/// This node automatically disposes itself when playback ends.
/// </remarks>
public sealed class OscillatorNode : AudioNode, IAudioScheduledSourceNode
{
    private OscillatorType _type = OscillatorType.Sine;
    private double _phase = 0.0;
    private AudioBuffer? _outputBuffer;
    private bool _hasStarted = false;
    private bool _hasStopped = false;
    private bool _endedRaised = false;
    private double _startTime = double.NaN;
    private double _stopTime = double.NaN;
    private long _renderedFrames = 0;

    public bool IsPlaying { get; private set; } = false;

    public event EventHandler? Ended;

    /// <summary>
    /// The frequency parameter in Hz.
    /// </summary>
    public AudioParam Frequency { get; }

    public OscillatorType Type
    {
        get => _type;
        set
        {
            var newType = value;
            void Do(AudioContextBase _) => _type = newType;
            Context.ExecuteOrPost(Do);
        }
    }

    public OscillatorNode(AudioContextBase context)
        : base(context, inputCount: 0, outputCount: 1, "Oscillator")
    {
        Frequency = CreateAudioParam(
            name: "frequency",
            defaultValue: 440f,
            minValue: 0f,
            maxValue: context.SampleRate / 2f,
            automationRate: AutomationRate.ARate);
    }

    public void Start(double when = 0, double offset = 0, double duration = double.NaN)
    {
        void Do(AudioContextBase _)
        {
            if (_hasStarted)
                throw new InvalidOperationException("OscillatorNode can only be started once.");

            _hasStarted = true;
            _phase = 0.0;
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
                    startFrame = (int)Math.Clamp(Math.Ceiling((_startTime - t0) * Context.SampleRate), 0, AudioBuffer.FramesPerBlock);
                }

                if (!double.IsNaN(_stopTime) && t0 < _stopTime && _stopTime < t1)
                {
                    endFrame = (int)Math.Clamp(Math.Floor((_stopTime - t0) * Context.SampleRate), 0, AudioBuffer.FramesPerBlock);
                }
            }
        }

        if (!shouldPlay)
        {
            _outputBuffer.Clear();
            Outputs[0].SetBuffer(_outputBuffer);

            IsPlaying = false;
            TryRaiseEndedAndDisconnect(t1);
            return;
        }

        var output = _outputBuffer.GetChannelSpan(0);
        var freqValues = Frequency.GetValues();

        for (int i = 0; i < startFrame; i++)
        {
            output[i] = 0f;
        }

        for (int i = startFrame; i < endFrame; i++)
        {
            output[i] = GenerateSample(_phase, _type);

            double phaseIncrement = (2.0 * Math.PI * freqValues[i]) / Context.SampleRate;
            _phase += phaseIncrement;

            if (_phase >= 2.0 * Math.PI)
                _phase -= 2.0 * Math.PI;
        }

        for (int i = endFrame; i < AudioBuffer.FramesPerBlock; i++)
        {
            output[i] = 0f;
        }

        _outputBuffer.MarkAsNonSilent();
        Outputs[0].SetBuffer(_outputBuffer);

        _renderedFrames += (endFrame - startFrame);
        IsPlaying = endFrame > startFrame;
        TryRaiseEndedAndDisconnect(t1);
    }

    private void TryRaiseEndedAndDisconnect(double blockEndTime)
    {
        if (_hasStarted && _hasStopped && !_endedRaised && !double.IsNaN(_stopTime) && blockEndTime >= _stopTime)
        {
            _endedRaised = true;
            IsPlaying = false;
            Ended?.Invoke(this, EventArgs.Empty);
            Dispose();
        }
    }

    private static float GenerateSample(double phase, OscillatorType type)
    {
        switch (type)
        {
            case OscillatorType.Sine:
                return (float)Math.Sin(phase);

            case OscillatorType.Square:
                return phase < Math.PI ? 1.0f : -1.0f;

            case OscillatorType.Sawtooth:
                return (float)(2.0 * (phase / (2.0 * Math.PI)) - 1.0);

            case OscillatorType.Triangle:
                {
                    double t = phase / (2.0 * Math.PI);
                    return (float)(4.0 * Math.Abs(t - Math.Floor(t + 0.5)) - 1.0);
                }

            default:
                return 0f;
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

public enum OscillatorType
{
    Sine,
    Square,
    Sawtooth,
    Triangle
}
