using System;
using System.Threading.Tasks;
using GraphAudio.Core;
using GraphAudio.Nodes;
using GraphAudio.Kit.DataProviders;

namespace GraphAudio.Kit;

/// <summary>
/// A reverb effect that uses convolution.
/// </summary>
public sealed class ReverbEffect : Effect
{
    private readonly GainNode _inputSplit;
    private readonly GainNode _outputMerge;
    private readonly GainNode _dryGain;
    private readonly GainNode _wetGain;
    private readonly ConvolverNode _convolver;

    /// <inheritdoc/>
    public override AudioNode Input => _inputSplit;

    /// <inheritdoc/>
    public override AudioNode Output => _outputMerge;

    /// <summary>
    /// The gain of the dry (unprocessed) signal.
    /// </summary>
    public AudioParam Dry => _dryGain.Gain;

    /// <summary>
    /// The gain of the wet (reverb) signal.
    /// </summary>
    public AudioParam Wet => _wetGain.Gain;

    /// <summary>
    /// Whether to normalize the impulse response when setting it.
    /// </summary>
    public bool Normalize => _convolver.Normalize;

    /// <summary>
    /// Whether to treat 4-channel impulse responses as True Stereo.
    /// </summary>
    public bool EnableTrueStereo => _convolver.EnableTrueStereo;

    public ReverbEffect(AudioEngine engine) : base(engine)
    {
        _inputSplit = new GainNode(Context);
        _outputMerge = new GainNode(Context);
        _dryGain = new GainNode(Context);
        _wetGain = new GainNode(Context);
        _convolver = new ConvolverNode(Context);

        _inputSplit.Connect(_dryGain);
        _dryGain.Connect(_outputMerge);

        _inputSplit.Connect(_convolver);
        _convolver.Connect(_wetGain);
        _wetGain.Connect(_outputMerge);
    }

    /// <summary>
    /// Sets the impulse response buffer for the reverb.
    /// </summary>
    public void SetImpulseResponse(PlayableAudioBuffer buffer, bool normalize = true, bool enableTrueStereo = true)
    {
        _convolver.Normalize = normalize;
        _convolver.EnableTrueStereo = enableTrueStereo;
        _convolver.Buffer = buffer;
    }

    /// <summary>
    /// Asynchronously loads and sets the impulse response from afile path.
    /// </summary>
    public async Task SetImpulseResponseAsync(string path, bool normalize = true, bool enableTrueStereo = true)
    {
        if (Engine.DataProvider is null)
            throw new InvalidOperationException("No data provider is configured on the AudioEngine.");

        var buffer = await Engine.DataProvider.GetPlayableBufferAsync(path);
        SetImpulseResponse(buffer, normalize, enableTrueStereo);
    }

    protected override void OnDispose()
    {
        _inputSplit.Dispose();
        _outputMerge.Dispose();
        _dryGain.Dispose();
        _wetGain.Dispose();
        _convolver.Dispose();
    }
}
