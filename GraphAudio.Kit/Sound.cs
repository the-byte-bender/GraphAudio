using System;
using System.Numerics;
using System.Threading.Tasks;
using GraphAudio.Kit.SpatialBlendControllers;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// The mixing state of a sound.
/// </summary>
public enum SoundMixState
{
    /// <summary>
    /// The sound is passed through directly without spatialization.
    /// </summary>
    Direct,

    /// <summary>
    /// The sound is spatialized in 3d space with hrtf.
    /// </summary>
    BinoralSpatialized,

    /// <summary>
    /// The sound is spatialized in stereo using linear steps based on distance from the listener. Good for 2d panning.
    /// Uses a <see cref="StereoPannerNode"/> and adjusts gain and pitch.
    /// </summary>
    /// <remarks>
    /// Spatial Blend, Occlusion, Transmission, and Distance Models are NOT supported in this mode.
    /// Use <see cref="Sound.StepLinearConfig"/> to customize behavior.
    /// </remarks>
    StepLinearSpatialized
}

/// <summary>
/// An individual playable sound instance.
/// </summary>
public abstract class Sound : IDisposable
{
    private float _gain = 1.0f;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _orientation = Vector3.UnitZ;
    private bool _disposed;
    private SpatialAnchor? _anchor;
    private ulong _lastAnchorVersion;

    private readonly AudioEngine _engine;
    private readonly GainNode _gainNode;
    private readonly SpatialPannerNode? _spatialPanner;
    private readonly StereoPannerNode? _stereoPanner;
    private readonly AudioNode _output;
    private readonly EffectChain _effects;

    /// <summary>
    /// The audio engine this sound belongs to.
    /// </summary>
    public AudioEngine Engine => _engine;

    /// <summary>
    /// Whether this sound has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Whether this sound is a one-shot (automatically disposed when finished playing).
    /// </summary>
    public bool IsOneShot { get; internal set; }

    /// <summary>
    /// Whether the sound is currently playing.
    /// </summary>
    public abstract bool IsPlaying { get; }

    /// <summary>
    /// Whether the sound should loop when it reaches the end.
    /// </summary>
    public abstract bool IsLooping { get; set; }

    /// <summary>
    /// The playback rate (1.0 is normal speed)
    /// </summary>
    public abstract float PlaybackRate { get; set; }

    /// <summary>
    /// The total duration of the audio.
    /// </summary>
    public abstract TimeSpan Duration { get; }

    /// <summary>
    /// Seeks to a specific position in the audio.
    /// </summary>
    public abstract void Seek(TimeSpan position);

    /// <summary>
    /// The gain of the sound, from 0.0 (silent) to 1.0 (full volume).
    /// </summary>
    public float Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            _gainNode.Gain.Value = _gain;
        }
    }

    /// <summary>
    /// The position of the sound in 3D space. When an Anchor is set, this acts as an offset from the anchor's position.
    /// Only applicable if the sound is spatialized.
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            UpdateSpatialPosition();
        }
    }

    /// <summary>
    /// The spatial anchor to follow. When set, the sound's position will be relative to the anchor's position.
    /// Set to null to use absolute positioning.
    /// </summary>
    public SpatialAnchor? Anchor
    {
        get => _anchor;
        set
        {
            _anchor = value;
            if (_anchor is not null)
            {
                _lastAnchorVersion = 0;
            }
        }
    }

    /// <summary>
    /// The orientation of the sound in 3D space. Only applicable if the sound is spatialized.
    /// </summary>
    public Vector3 Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            if (_spatialPanner is not null)
            {
                _spatialPanner.OrientationX.Value = value.X;
                _spatialPanner.OrientationY.Value = value.Y;
                _spatialPanner.OrientationZ.Value = value.Z;
            }
        }
    }

    /// <summary>
    /// The reference distance for the sound's spatialization. Only applicable if the sound is spatialized.
    /// </summary>
    public float RefDistance
    {
        get => _spatialPanner?.RefDistance.Value ?? 1.0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.RefDistance.Value = value;
            }
        }
    }

    /// <summary>
    /// The rolloff factor for the sound's spatialization. Only applicable if the sound is spatialized.
    /// </summary>
    public float RolloffFactor
    {
        get => _spatialPanner?.RolloffFactor.Value ?? 1.0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.RolloffFactor.Value = value;
            }
        }
    }

    /// <summary>
    /// The maximum distance for the sound's spatialization. Only applicable if the sound is spatialized.
    /// </summary>
    public float MaxDistance
    {
        get => _spatialPanner?.MaxDistance.Value ?? 10000f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.MaxDistance.Value = value;
            }
        }
    }

    /// <summary>
    /// The distance model used for the sound's spatialization. Only applicable if the sound is spatialized.
    /// </summary>
    public SpatialPannerNode.DistanceModelType DistanceModel
    {
        get => _spatialPanner?.DistanceModel ?? SpatialPannerNode.DistanceModelType.Inverse;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.DistanceModel = value;
            }
        }
    }

    /// <summary>
    /// The inner angle (in degrees) of the sound cone. Sound inside this cone is at full volume. Only applicable if the sound is spatialized.
    /// </summary>
    public float ConeInnerAngle
    {
        get => _spatialPanner?.ConeInnerAngle.Value ?? 360f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.ConeInnerAngle.Value = value;
            }
        }
    }

    /// <summary>
    /// The outer angle (in degrees) of the sound cone. Sound outside this cone is attenuated. Only applicable if the sound is spatialized.
    /// </summary>
    public float ConeOuterAngle
    {
        get => _spatialPanner?.ConeOuterAngle.Value ?? 360f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.ConeOuterAngle.Value = value;
            }
        }
    }

    /// <summary>
    /// The gain multiplier applied to sound outside the outer cone angle. Only applicable if the sound is spatialized.
    /// </summary>
    public float ConeOuterGain
    {
        get => _spatialPanner?.ConeOuterGain.Value ?? 0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.ConeOuterGain.Value = value;
            }
        }
    }

    /// <summary>
    /// The occlusion factor of the sound, from 0.0 (no occlusion) to 1.0 (fully occluded).
    /// Only applicable if the sound is spatialized.
    /// </summary>
    public float Occlusion
    {
        get => _spatialPanner?.Occlusion.Value ?? 0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.Occlusion.Value = value;
            }
        }
    }

    /// <summary>
    /// The transmission factor for low frequencies, from 0.0 (opaque) to 1.0 (transparent).
    /// Only applicable if the sound is spatialized and Occlusion > 0.
    /// </summary>
    public float TransmissionLow
    {
        get => _spatialPanner?.TransmissionLow.Value ?? 0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.TransmissionLow.Value = value;
            }
        }
    }

    /// <summary>
    /// The transmission factor for mid frequencies, from 0.0 (opaque) to 1.0 (transparent).
    /// Only applicable if the sound is spatialized and Occlusion > 0.
    /// </summary>
    public float TransmissionMid
    {
        get => _spatialPanner?.TransmissionMid.Value ?? 0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.TransmissionMid.Value = value;
            }
        }
    }

    /// <summary>
    /// The transmission factor for high frequencies, from 0.0 (opaque) to 1.0 (transparent).
    /// Only applicable if the sound is spatialized and Occlusion > 0.
    /// </summary>
    public float TransmissionHigh
    {
        get => _spatialPanner?.TransmissionHigh.Value ?? 0f;
        set
        {
            if (_spatialPanner is not null)
            {
                _spatialPanner.TransmissionHigh.Value = value;
            }
        }
    }

    /// <summary>
    /// Sets the transmission factors for all frequency bands.
    /// </summary>
    public void SetTransmission(float low, float mid, float high)
    {
        if (_spatialPanner is not null)
        {
            _spatialPanner.TransmissionLow.Value = low;
            _spatialPanner.TransmissionMid.Value = mid;
            _spatialPanner.TransmissionHigh.Value = high;
        }
    }

    /// <summary>
    /// The mixing state of the sound.
    /// </summary>
    public SoundMixState MixState { get; }

    /// <summary>
    /// The bus this sound is attached to.
    /// </summary>
    public AudioBus Bus { get; private set; }

    /// <summary>
    /// The effect chain for this sound.
    /// </summary>
    public EffectChain Effects => _effects;

    /// <summary>
    /// The input gain node.
    /// </summary>
    protected GainNode Input => _gainNode;

    /// <summary>
    /// Controller used to compute spatial blend from distance. Only effective when spatialized.
    /// </summary>
    public ISpatialBlendController SpatialBlendController { get; set; }

    /// <summary>
    /// Configuration for step-linear spatialization. Only effective when <see cref="MixState"/> is <see cref="SoundMixState.StepLinearSpatialized"/>.
    /// </summary>
    public StepLinearConfig StepLinearConfig { get; set; } = DefaultStepLinearConfig;

    public static ISpatialBlendController DefaultSpatialBlendController { get; set; } = SpatialBlendControllers.DefaultSpatialBlendController.Instance;

    /// <summary>
    /// The global default configuration for step-linear spatialization.
    /// </summary>
    public static StepLinearConfig DefaultStepLinearConfig { get; set; } = StepLinearConfig.Default;

    protected Sound(AudioEngine engine, SoundMixState mixState, AudioBus? bus = null)
    {
        _engine = engine;
        MixState = mixState;
        Bus = bus ?? engine.MasterBus;

        _gainNode = new GainNode(engine.Context);

        SpatialBlendController = DefaultSpatialBlendController;

        switch (mixState)
        {
            case SoundMixState.Direct:
                _output = _gainNode;
                break;

            case SoundMixState.BinoralSpatialized:
                _spatialPanner = new SpatialPannerNode(engine.Context);
                _output = _spatialPanner;
                break;

            case SoundMixState.StepLinearSpatialized:
                _stereoPanner = new StereoPannerNode(engine.Context);
                _output = _stereoPanner;
                break;

            default:
                throw new InvalidOperationException($"Unsupported mix state: {mixState}");
        }

        if (_output != _gainNode)
        {
            _effects = new EffectChain(engine, _gainNode, _output);
        }
        else
        {
            // For direct mode, effects go between gain and bus
            _effects = new EffectChain(engine, _gainNode, Bus.Input);
        }

        if (_output != _gainNode)
        {
            _output.Connect(Bus.Input);
            UpdateSpatialPosition();
            UpdateSpatialBlend();
        }
    }

    /// <summary>
    /// Changes the bus this sound is attached to.
    /// </summary>
    public void SetBus(AudioBus bus)
    {
        if (bus.Engine != Engine) throw new ArgumentException("Bus must belong to the same engine.", nameof(bus));

        Bus = bus;

        if (_output == _gainNode)
        {
            _effects.UpdateEndpoints(_gainNode, bus.Input);
        }
        else
        {
            _output.Disconnect();
            _output.Connect(bus.Input);
        }
    }

    /// <summary>
    /// Changes the bus this sound is attached to by path.
    /// </summary>
    public void SetBus(string busPath)
    {
        var bus = Engine.GetBus(busPath);
        SetBus(bus);
    }

    internal void Update()
    {
        if (_anchor is not null)
        {
            var currentVersion = _anchor.Version;
            if (currentVersion != _lastAnchorVersion)
            {
                _lastAnchorVersion = currentVersion;
                UpdateSpatialPosition();
            }
        }

        if (MixState == SoundMixState.StepLinearSpatialized)
        {
            UpdateStepLinear();
        }
        else
        {
            UpdateSpatialBlend();
        }

        DoUpdate();
    }

    private void UpdateStepLinear()
    {
        if (_stereoPanner is null)
            return;

        var finalPosition = _anchor is not null
            ? _anchor.Position + _position
            : _position;

        var result = StepLinearCalculator.Calculate(
            _engine.ListenerPosition,
            finalPosition,
            StepLinearConfig,
            0.0f,
            Gain,
            PlaybackRate);

        _stereoPanner.Pan.Value = result.Pan;
        _gainNode.Gain.Value = result.Gain;
        ApplyEffectivePlaybackRate(result.Pitch);
    }

    private void UpdateSpatialPosition()
    {
        if (_spatialPanner is null)
            return;

        var finalPosition = _anchor is not null
            ? _anchor.Position + _position
            : _position;

        _spatialPanner.PositionX.Value = finalPosition.X;
        _spatialPanner.PositionY.Value = finalPosition.Y;
        _spatialPanner.PositionZ.Value = finalPosition.Z;
        UpdateSpatialBlend();
    }

    private void UpdateSpatialBlend()
    {
        if (_spatialPanner is null)
            return;

        var finalPosition = _anchor is not null
            ? _anchor.Position + _position
            : _position;

        var listener = _engine.ListenerPosition;
        var delta = finalPosition - listener;
        var distance = delta.Length();
        var blend = Math.Clamp(SpatialBlendController.GetBlend(distance), 0f, 1f);
        _spatialPanner.SpatialBlend.Value = blend;
    }

    /// <summary>
    /// Start, resume, or restart playback.
    /// </summary>
    public void Play(double fadeInDuration = 0)
    {
        if (fadeInDuration > 0)
        {
            _gainNode.Gain.SetValueAtTime(0.0001f, Engine.Context.CurrentTime);
            DoPlay();
            _gainNode.Gain.ExponentialRampToValueAtTime(Gain, Engine.Context.CurrentTime + fadeInDuration);
            return;
        }

        DoPlay();
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        DoPause();
    }

    /// <summary>
    /// Pause playback after fading out over the specified duration.
    /// </summary>
    public async Task Pause(double fadeOutDuration)
    {
        if (fadeOutDuration > 0)
        {
            _gainNode.Gain.SetValueAtTime(_gainNode.Gain.Value, Engine.Context.CurrentTime);
            _gainNode.Gain.ExponentialRampToValueAtTime(0.0001f, Engine.Context.CurrentTime + fadeOutDuration);
            await Task.Delay((int)(fadeOutDuration * 1000)).ConfigureAwait(false);
        }

        DoPause();
    }

    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        DoStop();
    }

    /// <summary>
    /// Stop playback after fading out over the specified duration.
    /// </summary>
    public async Task Stop(double fadeOutDuration)
    {
        if (fadeOutDuration > 0)
        {
            _gainNode.Gain.SetValueAtTime(_gainNode.Gain.Value, Engine.Context.CurrentTime);
            _gainNode.Gain.ExponentialRampToValueAtTime(0.0001f, Engine.Context.CurrentTime + fadeOutDuration);
            await Task.Delay((int)(fadeOutDuration * 1000)).ConfigureAwait(false);
        }

        DoStop();
    }

    /// <summary>
    /// Configure the sound cone for directional audio.
    /// Only applicable if the sound is spatialized.
    /// </summary>
    public void SetCone(float innerAngle, float outerAngle, float outerGain)
    {
        if (_spatialPanner is null)
            return;

        ConeInnerAngle = innerAngle;
        ConeOuterAngle = outerAngle;
        ConeOuterGain = Math.Clamp(outerGain, 0f, 1f);
    }

    /// <summary>
    /// Configure distance attenuation for the sound.
    /// Only applicable if the sound is spatialized.
    /// </summary>
    public void SetDistanceModel(SpatialPannerNode.DistanceModelType model, float refDistance, float maxDistance, float rolloffFactor)
    {
        if (_spatialPanner is null)
            return;

        DistanceModel = model;
        RefDistance = refDistance;
        MaxDistance = maxDistance;
        RolloffFactor = rolloffFactor;
    }

    /// <summary>
    /// Releases all resources used by the sound.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (IsPlaying)
            {
                DoStop();
            }

            _gainNode.Disconnect();
            _output.Disconnect();

            _spatialPanner?.Dispose();
            _stereoPanner?.Dispose();
            _gainNode.Dispose();

            OnDispose();
        }

        _disposed = true;
    }

    protected virtual void OnDispose()
    {
    }

    /// <summary>
    /// Throws an ObjectDisposedException if this sound has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    protected virtual void DoUpdate()
    { }

    /// <summary>
    /// Applies the effective playback rate to the underlying audio nodes.
    /// This is used by <see cref="SoundMixState.StepLinearSpatialized"/> to apply pitch shifts.
    /// </summary>
    protected virtual void ApplyEffectivePlaybackRate(float rate)
    { }

    protected abstract void DoPlay();
    protected abstract void DoPause();
    protected abstract void DoStop();
}
