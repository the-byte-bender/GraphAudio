using System;
using System.Runtime.CompilerServices;
using System.Threading;
using GraphAudio.Nodes;

namespace GraphAudio.Core;

/// <summary>
/// Represents an audio parameter that can be automated.
/// </summary>
public sealed class AudioParam
{
    private readonly AudioNode _owner;
    private readonly AudioNodeInput _input;
    private readonly string _name;
    private readonly float _defaultValue;
    private readonly float _minValue;
    private readonly float _maxValue;
    private readonly AutomationRate _automationRate;
    private float _value;
    private float[] _computedValues;
    private AutomationEvent[] _events;
    private double _currentTime;

    public string Name => _name;
    public float DefaultValue => _defaultValue;
    public float MinValue => _minValue;
    public float MaxValue => _maxValue;
    public AutomationRate AutomationRate => _automationRate;

    /// <summary>
    /// The current parameter value. Setting this cancels all scheduled automation events.
    /// </summary>
    public float Value
    {
        get => Volatile.Read(ref _value);
        set
        {
            var clamped = Math.Clamp(value, _minValue, _maxValue);
            Volatile.Write(ref _value, clamped);
            for (; ; )
            {
                var cur = Volatile.Read(ref _events);
                if (ReferenceEquals(cur, Array.Empty<AutomationEvent>())) break;
                var prior = Interlocked.CompareExchange(ref _events, Array.Empty<AutomationEvent>(), cur);
                if (ReferenceEquals(prior, cur)) break;
            }
        }
    }

    internal AudioParam(
        AudioNode owner,
        string name,
        float defaultValue,
        float minValue,
        float maxValue,
        AutomationRate automationRate)
    {
        _owner = owner;
        _name = name;
        _defaultValue = defaultValue;
        _minValue = minValue;
        _maxValue = maxValue;
        _automationRate = automationRate;
        _value = defaultValue;
        _computedValues = new float[AudioBuffer.FramesPerBlock];
        _events = Array.Empty<AutomationEvent>();
        _input = new AudioNodeInput(owner, -1, owner.Context.BufferPool);
        _input.SetChannelCount(1);
        _input.SetChannelCountMode(ChannelCountMode.Explicit);
    }

    internal void ConnectFrom(AudioNodeOutput output)
    {
        void Do(AudioContextBase _) => output.ConnectTo(_input);
        _owner.Context.ExecuteOrPost(Do);
    }

    internal void DisconnectFrom(AudioNodeOutput output)
    {
        void Do(AudioContextBase _) => output.DisconnectFrom(_input);
        _owner.Context.ExecuteOrPost(Do);
    }
    internal void DisconnectAll() => _input.DisconnectAll();

    /// <summary>
    /// Get the computed values for this block. Must be called during Process().
    /// Returns a span of 128 values for a-rate params, or a repeated k-rate value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<float> GetValues() => _computedValues;

    internal void ComputeValues(int blockNumber, double blockTime)
    {
        _currentTime = blockTime;

        bool hasModulation = _input.ConnectedOutputs.Count > 0;
        if (hasModulation)
        {
            _input.Pull(blockNumber, blockTime);
        }

        if (_automationRate == AutomationRate.ARate)
        {
            ComputeARate(hasModulation);
        }
        else
        {
            ComputeKRate(hasModulation);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComputeARate(bool hasModulation)
    {
        double deltaTime = 1.0 / _owner.Context.SampleRate;

        for (int i = 0; i < AudioBuffer.FramesPerBlock; i++)
        {
            double sampleTime = _currentTime + i * deltaTime;
            float intrinsicValue = ComputeValueAtTime(sampleTime);

            if (hasModulation)
            {
                var inputBuffer = _input.Buffer;
                if (inputBuffer is not null && !inputBuffer.IsSilent)
                {
                    float modulation = inputBuffer.GetChannelSpan(0)[i];
                    _computedValues[i] = Math.Clamp(intrinsicValue + modulation, _minValue, _maxValue);
                }
                else
                {
                    _computedValues[i] = intrinsicValue;
                }
            }
            else
            {
                _computedValues[i] = intrinsicValue;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComputeKRate(bool hasModulation)
    {
        float intrinsicValue = ComputeValueAtTime(_currentTime);

        if (hasModulation)
        {
            var inputBuffer = _input.Buffer;
            if (inputBuffer is not null && !inputBuffer.IsSilent)
            {
                float modulation = inputBuffer.GetChannelSpan(0)[0];
                float finalValue = Math.Clamp(intrinsicValue + modulation, _minValue, _maxValue);
                _computedValues.AsSpan().Fill(finalValue);
            }
            else
            {
                _computedValues.AsSpan().Fill(intrinsicValue);
            }
        }
        else
        {
            _computedValues.AsSpan().Fill(intrinsicValue);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ComputeValueAtTime(double time)
    {
        var events = Volatile.Read(ref _events);
        int count = events.Length;
        if (count == 0)
            return Volatile.Read(ref _value);

        float valueAtBoundary = Volatile.Read(ref _value);
        for (int i = 0; i < count; i++)
        {
            ref var evt = ref events[i];

            if (time < evt.Time)
            {
                if (i == 0)
                    return valueAtBoundary;

                ref var prev = ref events[i - 1];
                if (evt.Type == AutomationEventType.LinearRamp)
                    return InterpolateLinear(prev.Value, prev.Time, evt.Value, evt.Time, time);
                if (evt.Type == AutomationEventType.ExponentialRamp)
                    return InterpolateExponential(prev.Value, prev.Time, evt.Value, evt.Time, time);
                if (prev.Type == AutomationEventType.SetTarget)
                    return ComputeSetTargetFromBaseline(ref prev, valueAtBoundary, time);
                return prev.Value;
            }

            switch (evt.Type)
            {
                case AutomationEventType.SetValue:
                case AutomationEventType.LinearRamp:
                case AutomationEventType.ExponentialRamp:
                    valueAtBoundary = evt.Value;
                    break;
                case AutomationEventType.SetTarget:
                    break;
            }
        }

        ref var lastEvent = ref events[count - 1];
        return lastEvent.Type switch
        {
            AutomationEventType.SetValue => lastEvent.Value,
            AutomationEventType.LinearRamp => lastEvent.Value,
            AutomationEventType.ExponentialRamp => lastEvent.Value,
            AutomationEventType.SetTarget => ComputeSetTargetFromBaseline(ref lastEvent, valueAtBoundary, time),
            _ => Volatile.Read(ref _value)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterpolateLinear(float v0, double t0, float v1, double t1, double t)
    {
        double u = (t - t0) / (t1 - t0);
        u = Math.Clamp(u, 0.0, 1.0);
        return (float)(v0 + (v1 - v0) * u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterpolateExponential(float v0, double t0, float v1, double t1, double t)
    {
        if (v0 <= 0 || v1 <= 0)
        {
            return InterpolateLinear(v0, t0, v1, t1, t);
        }
        double u = (t - t0) / (t1 - t0);
        u = Math.Clamp(u, 0.0, 1.0);
        return (float)(v0 * Math.Pow(v1 / v0, u));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeSetTargetFromBaseline(ref AutomationEvent evt, float baselineAtStart, double time)
    {
        double elapsed = time - evt.Time;
        if (elapsed <= 0) return baselineAtStart;

        double timeConstant = Math.Max(evt.TimeConstant, 0.001);
        return (float)(evt.Target + (baselineAtStart - evt.Target) * Math.Exp(-elapsed / timeConstant));
    }

    /// <summary>
    /// Schedules a parameter value change at a specific time.
    /// </summary>
    public void SetValueAtTime(float value, double startTime)
    {
        value = Math.Clamp(value, _minValue, _maxValue);
        AddEvent(new AutomationEvent
        {
            Type = AutomationEventType.SetValue,
            Value = value,
            Time = startTime
        });
    }

    /// <summary>
    /// Schedules a linear continuous change in value from the previous event's value to the given value.
    /// </summary>
    public void LinearRampToValueAtTime(float value, double endTime)
    {
        value = Math.Clamp(value, _minValue, _maxValue);
        AddEvent(new AutomationEvent
        {
            Type = AutomationEventType.LinearRamp,
            Value = value,
            Time = endTime
        });
    }

    /// <summary>
    /// Schedules an exponential continuous change in value from the previous event's value to the given value.
    /// </summary>
    public void ExponentialRampToValueAtTime(float value, double endTime)
    {
        value = Math.Clamp(value, _minValue, _maxValue);
        if (value <= 0f)
            throw new ArgumentException("Exponential ramp target must be > 0", nameof(value));

        AddEvent(new AutomationEvent
        {
            Type = AutomationEventType.ExponentialRamp,
            Value = value,
            Time = endTime
        });
    }

    /// <summary>
    /// Start exponentially approaching the target value at the given time with a time constant.
    /// </summary>
    public void SetTargetAtTime(float target, double startTime, double timeConstant)
    {
        target = Math.Clamp(target, _minValue, _maxValue);
        AddEvent(new AutomationEvent
        {
            Type = AutomationEventType.SetTarget,
            Target = target,
            Time = startTime,
            TimeConstant = timeConstant
        });
    }

    /// <summary>
    /// Cancels all scheduled parameter changes with times greater than or equal to cancelTime.
    /// </summary>
    public void CancelScheduledValues(double cancelTime)
    {
        for (; ; )
        {
            var src = Volatile.Read(ref _events);
            if (src.Length == 0) return;
            int survivors = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i].Time < cancelTime) survivors++;
                else break;
            }
            if (survivors == src.Length) return;
            var dst = new AutomationEvent[survivors];
            if (survivors > 0)
                Array.Copy(src, 0, dst, 0, survivors);
            var prior = Interlocked.CompareExchange(ref _events, dst, src);
            if (ReferenceEquals(prior, src)) return;
        }
    }

    private void AddEvent(AutomationEvent evt)
    {
        for (; ; )
        {
            var src = Volatile.Read(ref _events);
            int n = src.Length;
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (evt.Time < src[mid].Time) hi = mid; else lo = mid + 1;
            }
            var dst = new AutomationEvent[n + 1];
            if (lo > 0) Array.Copy(src, 0, dst, 0, lo);
            dst[lo] = evt;
            if (lo < n) Array.Copy(src, lo, dst, lo + 1, n - lo);
            var prior = Interlocked.CompareExchange(ref _events, dst, src);
            if (ReferenceEquals(prior, src)) return;
        }
    }

    internal void Dispose()
    {
        _input.DisconnectAll();
        _input.Dispose();
    }

    private struct AutomationEvent
    {
        public AutomationEventType Type;
        public float Value;
        public float Target;
        public double Time;
        public double TimeConstant;
    }

    private enum AutomationEventType
    {
        SetValue,
        LinearRamp,
        ExponentialRamp,
        SetTarget
    }
}

/// <summary>
/// Determines how often a parameter's values are computed.
/// </summary>
public enum AutomationRate
{
    /// <summary>
    /// computed every sample.
    /// </summary>
    ARate,

    /// <summary>
    /// computed once per block (128 samples)
    /// </summary>
    KRate
}
