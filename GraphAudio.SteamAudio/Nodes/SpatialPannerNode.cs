using System;
using GraphAudio.Core;
using GraphAudio.SteamAudio;
using SteamAudio;

namespace GraphAudio.Nodes;

/// <summary>
/// A spatial panner node that uses SteamAudio for HRTF-based 3D audio positioning.
/// </summary>
public sealed unsafe class SpatialPannerNode : SteamAudioNodeBase
{
    private readonly IPL.Hrtf _hrtf;
    private IPL.BinauralEffect _binauralEffect;
    private IPL.DirectEffect _directEffect;

    public AudioParam PositionX { get; }
    public AudioParam PositionY { get; }
    public AudioParam PositionZ { get; }
    public AudioParam OrientationX { get; }
    public AudioParam OrientationY { get; }
    public AudioParam OrientationZ { get; }
    public AudioParam RefDistance { get; }
    public AudioParam MaxDistance { get; }
    public AudioParam RolloffFactor { get; }
    public AudioParam ConeInnerAngle { get; }
    public AudioParam ConeOuterAngle { get; }
    public AudioParam ConeOuterGain { get; }

    private DistanceModelType _distanceModel = DistanceModelType.Inverse;
    public DistanceModelType DistanceModel
    {
        get => _distanceModel;
        set => _distanceModel = value;
    }

    public enum DistanceModelType
    {
        Linear,
        Inverse,
        Exponential
    }

    public SpatialPannerNode(AudioContextBase context)
        : base(context, inputChannelCount: 1, outputChannelCount: 2, name: "SpatialPanner")
    {
        _hrtf = context.GetHrtf();

        var audioSettings = new IPL.AudioSettings
        {
            SamplingRate = context.SampleRate,
            FrameSize = AudioBuffer.FramesPerBlock
        };

        var binauralSettings = new IPL.BinauralEffectSettings
        {
            Hrtf = _hrtf
        };

        var error = IPL.BinauralEffectCreate(IplContext, in audioSettings, in binauralSettings, out _binauralEffect);
        if (error != IPL.Error.Success)
            throw new InvalidOperationException($"Failed to create binaural effect: {error}");

        var directSettings = new IPL.DirectEffectSettings
        {
            NumChannels = 1
        };

        error = IPL.DirectEffectCreate(IplContext, in audioSettings, in directSettings, out _directEffect);
        if (error != IPL.Error.Success)
        {
            IPL.BinauralEffectRelease(ref _binauralEffect);
            throw new InvalidOperationException($"Failed to create direct effect: {error}");
        }

        PositionX = CreateAudioParam("positionX", 0f, float.MinValue, float.MaxValue, AutomationRate.KRate);
        PositionY = CreateAudioParam("positionY", 0f, float.MinValue, float.MaxValue, AutomationRate.KRate);
        PositionZ = CreateAudioParam("positionZ", 0f, float.MinValue, float.MaxValue, AutomationRate.KRate);
        OrientationX = CreateAudioParam("orientationX", 1f, -1f, 1f, AutomationRate.KRate);
        OrientationY = CreateAudioParam("orientationY", 0f, -1f, 1f, AutomationRate.KRate);
        OrientationZ = CreateAudioParam("orientationZ", 0f, -1f, 1f, AutomationRate.KRate);
        RefDistance = CreateAudioParam("refDistance", 1f, 0f, float.MaxValue, AutomationRate.KRate);
        MaxDistance = CreateAudioParam("maxDistance", 10000f, 0f, float.MaxValue, AutomationRate.KRate);
        RolloffFactor = CreateAudioParam("rolloffFactor", 1f, 0f, float.MaxValue, AutomationRate.KRate);
        ConeInnerAngle = CreateAudioParam("coneInnerAngle", 360f, 0f, 360f, AutomationRate.KRate);
        ConeOuterAngle = CreateAudioParam("coneOuterAngle", 360f, 0f, 360f, AutomationRate.KRate);
        ConeOuterGain = CreateAudioParam("coneOuterGain", 0f, 0f, 1f, AutomationRate.KRate);

        Inputs[0].SetChannelCount(1);
        Inputs[0].SetChannelCountMode(ChannelCountMode.Explicit);
    }

    protected override void ProcessSteamAudio(ref IPL.AudioBuffer input, ref IPL.AudioBuffer output)
    {
        var posX = PositionX.GetValues()[0];
        var posY = PositionY.GetValues()[0];
        var posZ = PositionZ.GetValues()[0];
        var oriX = OrientationX.GetValues()[0];
        var oriY = OrientationY.GetValues()[0];
        var oriZ = OrientationZ.GetValues()[0];
        var refDist = RefDistance.GetValues()[0];
        var maxDist = MaxDistance.GetValues()[0];
        var rolloff = RolloffFactor.GetValues()[0];

        var sourcePos = new IPL.Vector3 { X = posX, Y = posY, Z = posZ };
        var listenerTransform = Context.GetListenerTransform();

        var worldDirection = new IPL.Vector3
        {
            X = sourcePos.X - listenerTransform.Origin.X,
            Y = sourcePos.Y - listenerTransform.Origin.Y,
            Z = sourcePos.Z - listenerTransform.Origin.Z
        };

        float distance = MathF.Sqrt(worldDirection.X * worldDirection.X + worldDirection.Y * worldDirection.Y + worldDirection.Z * worldDirection.Z);

        IPL.Vector3 direction;
        if (distance > 0.0001f)
        {
            float invDist = 1.0f / distance;
            worldDirection.X *= invDist;
            worldDirection.Y *= invDist;
            worldDirection.Z *= invDist;

            direction = new IPL.Vector3
            {
                X = worldDirection.X * listenerTransform.Right.X + worldDirection.Y * listenerTransform.Right.Y + worldDirection.Z * listenerTransform.Right.Z,
                Y = worldDirection.X * listenerTransform.Up.X + worldDirection.Y * listenerTransform.Up.Y + worldDirection.Z * listenerTransform.Up.Z,
                Z = worldDirection.X * listenerTransform.Ahead.X + worldDirection.Y * listenerTransform.Ahead.Y + worldDirection.Z * listenerTransform.Ahead.Z
            };
        }
        else
        {
            direction = new IPL.Vector3 { X = 0, Y = 0, Z = -1 };
            distance = 0f;
        }

        var distModel = new IPL.DistanceAttenuationModel
        {
            Type = IPL.DistanceAttenuationModelType.InverseDistance,
            MinDistance = refDist,
            Callback = null,
            UserData = IntPtr.Zero,
            Dirty = false
        };

        float attenuation = IPL.DistanceAttenuationCalculate(IplContext, sourcePos, listenerTransform.Origin, in distModel);
        attenuation = ApplyDistanceModel(distance, refDist, maxDist, rolloff, attenuation);

        var directParams = new IPL.DirectEffectParams
        {
            Flags = IPL.DirectEffectFlags.ApplyDistanceAttenuation,
            TransmissionType = IPL.TransmissionType.FrequencyIndependent,
            DistanceAttenuation = attenuation,
            Directivity = 1.0f,
            Occlusion = 0f
        };

        directParams.AirAbsorption[0] = 1.0f;
        directParams.AirAbsorption[1] = 1.0f;
        directParams.AirAbsorption[2] = 1.0f;
        directParams.Transmission[0] = 1.0f;
        directParams.Transmission[1] = 1.0f;
        directParams.Transmission[2] = 1.0f;

        IPL.DirectEffectApply(_directEffect, ref directParams, ref input, ref input);

        var binauralParams = new IPL.BinauralEffectParams
        {
            Direction = direction,
            Interpolation = IPL.HrtfInterpolation.Bilinear,
            SpatialBlend = 1.0f,
            Hrtf = _hrtf,
            PeakDelays = IntPtr.Zero
        };
        IPL.BinauralEffectApply(_binauralEffect, ref binauralParams, ref input, ref output);
    }

    private float ApplyDistanceModel(float distance, float refDistance, float maxDistance, float rolloffFactor, float steamAudioAttenuation)
    {
        distance = Math.Clamp(distance, refDistance, maxDistance);
        float attenuation = 1.0f;

        switch (_distanceModel)
        {
            case DistanceModelType.Linear:
                attenuation = 1f - rolloffFactor * (distance - refDistance) / (maxDistance - refDistance);
                break;

            case DistanceModelType.Inverse:
                attenuation = steamAudioAttenuation;
                break;

            case DistanceModelType.Exponential:
                attenuation = MathF.Pow(distance / refDistance, -rolloffFactor);
                break;
        }

        return Math.Clamp(attenuation, 0f, 1f);
    }

    protected override void OnDispose()
    {
        if (_binauralEffect.Handle != IntPtr.Zero)
        {
            IPL.BinauralEffectRelease(ref _binauralEffect);
        }

        if (_directEffect.Handle != IntPtr.Zero)
        {
            IPL.DirectEffectRelease(ref _directEffect);
        }
        base.OnDispose();
    }
}
