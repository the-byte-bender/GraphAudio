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
    private IPL.DirectEffect _directEffectMono;
    private IPL.DirectEffect _directEffectStereo;

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
    public AudioParam SpatialBlend { get; }
    public AudioParam Occlusion { get; }
    public AudioParam TransmissionLow { get; }
    public AudioParam TransmissionMid { get; }
    public AudioParam TransmissionHigh { get; }

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
        : base(context, maxInputChannelCount: 2, outputChannelCount: 2, name: "SpatialPanner")
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

        var directSettingsMono = new IPL.DirectEffectSettings
        {
            NumChannels = 1
        };

        error = IPL.DirectEffectCreate(IplContext, in audioSettings, in directSettingsMono, out _directEffectMono);
        if (error != IPL.Error.Success)
        {
            IPL.BinauralEffectRelease(ref _binauralEffect);
            throw new InvalidOperationException($"Failed to create mono direct effect: {error}");
        }

        var directSettingsStereo = new IPL.DirectEffectSettings
        {
            NumChannels = 2
        };

        error = IPL.DirectEffectCreate(IplContext, in audioSettings, in directSettingsStereo, out _directEffectStereo);
        if (error != IPL.Error.Success)
        {
            IPL.BinauralEffectRelease(ref _binauralEffect);
            IPL.DirectEffectRelease(ref _directEffectMono);
            throw new InvalidOperationException($"Failed to create stereo direct effect: {error}");
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
        SpatialBlend = CreateAudioParam("spatialBlend", 1f, 0f, 1f, AutomationRate.KRate);
        Occlusion = CreateAudioParam("occlusion", 0f, 0f, 1f, AutomationRate.KRate);
        TransmissionLow = CreateAudioParam("transmissionLow", 0f, 0f, 1f, AutomationRate.KRate);
        TransmissionMid = CreateAudioParam("transmissionMid", 0f, 0f, 1f, AutomationRate.KRate);
        TransmissionHigh = CreateAudioParam("transmissionHigh", 0f, 0f, 1f, AutomationRate.KRate);

        Inputs[0].SetChannelCount(2);
        Inputs[0].SetChannelCountMode(ChannelCountMode.ClampedMax);
        Inputs[0].SetChannelInterpretation(ChannelInterpretation.Speakers);
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
        var occlusion = Occlusion.GetValues()[0];
        var transLow = TransmissionLow.GetValues()[0];
        var transMid = TransmissionMid.GetValues()[0];
        var transHigh = TransmissionHigh.GetValues()[0];

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

        float directivityValue = 1.0f;
        var innerAngle = ConeInnerAngle.GetValues()[0];
        var outerAngle = ConeOuterAngle.GetValues()[0];
        var outerGain = ConeOuterGain.GetValues()[0];

        if (innerAngle < 360f || outerAngle < 360f)
        {
            float oriMag = MathF.Sqrt(oriX * oriX + oriY * oriY + oriZ * oriZ);
            if (oriMag > 0.0001f)
            {
                float invOri = 1.0f / oriMag;
                float nOriX = oriX * invOri;
                float nOriY = oriY * invOri;
                float nOriZ = oriZ * invOri;

                float dot = nOriX * (-worldDirection.X) + nOriY * (-worldDirection.Y) + nOriZ * (-worldDirection.Z);
                dot = Math.Clamp(dot, -1f, 1f);

                float angleDeg = MathF.Acos(dot) * 180.0f / MathF.PI;
                float absAngle = MathF.Abs(angleDeg);

                float halfInner = innerAngle * 0.5f;
                float halfOuter = outerAngle * 0.5f;

                if (absAngle <= halfInner)
                {
                    directivityValue = 1.0f;
                }
                else if (absAngle >= halfOuter)
                {
                    directivityValue = outerGain;
                }
                else
                {
                    float t = (absAngle - halfInner) / (halfOuter - halfInner);
                    directivityValue = 1.0f + t * (outerGain - 1.0f);
                }
            }
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

        var flags = IPL.DirectEffectFlags.ApplyDistanceAttenuation;
        if (directivityValue < 0.999f) flags |= IPL.DirectEffectFlags.ApplyDirectivity;
        if (occlusion > 0.0f)
        {
            flags |= IPL.DirectEffectFlags.ApplyOcclusion;
            if (transLow > 0.0f || transMid > 0.0f || transHigh > 0.0f)
            {
                flags |= IPL.DirectEffectFlags.ApplyTransmission;
            }
        }

        var directParams = new IPL.DirectEffectParams
        {
            Flags = flags,
            TransmissionType = (flags & IPL.DirectEffectFlags.ApplyTransmission) != 0
                ? IPL.TransmissionType.FrequencyDependent
                : IPL.TransmissionType.FrequencyIndependent,
            DistanceAttenuation = attenuation,
            Directivity = directivityValue,
            Occlusion = occlusion
        };

        directParams.AirAbsorption[0] = 1.0f;
        directParams.AirAbsorption[1] = 1.0f;
        directParams.AirAbsorption[2] = 1.0f;
        directParams.Transmission[0] = transLow;
        directParams.Transmission[1] = transMid;
        directParams.Transmission[2] = transHigh;

        var directEffect = input.NumChannels == 1 ? _directEffectMono : _directEffectStereo;
        IPL.DirectEffectApply(directEffect, ref directParams, ref input, ref input);

        var spatialBlend = SpatialBlend.GetValues()[0];

        var binauralParams = new IPL.BinauralEffectParams
        {
            Direction = direction,
            Interpolation = IPL.HrtfInterpolation.Bilinear,
            SpatialBlend = spatialBlend,
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

        if (_directEffectMono.Handle != IntPtr.Zero)
        {
            IPL.DirectEffectRelease(ref _directEffectMono);
        }

        if (_directEffectStereo.Handle != IntPtr.Zero)
        {
            IPL.DirectEffectRelease(ref _directEffectStereo);
        }

        base.OnDispose();
    }
}
