using System;
using System.Collections.Generic;
using System.Linq;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// Manages a chain of audio effects.
/// </summary>
/// <remarks>
/// The chain takes ownership of the effects added to it. 
/// When an effect is removed or the chain is cleared, the effect is disposed.
/// </remarks>
public sealed class EffectChain : IDisposable
{
    private readonly List<Effect> _effects = new();
    private readonly AudioEngine _engine;
    private AudioNode _source;
    private AudioNode _destination;

    /// <summary>
    /// The effects in the chain, in processing order.
    /// </summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <summary>
    /// Gets the number of effects in the chain.
    /// </summary>
    public int Count => _effects.Count;

    internal EffectChain(AudioEngine engine, AudioNode source, AudioNode destination)
    {
        _engine = engine;
        _source = source;
        _destination = destination;
        _source.Connect(_destination);
    }

    /// <summary>
    /// Adds an effect to the end of the chain.
    /// </summary>
    public void Add(Effect effect)
    {
        Insert(_effects.Count, effect);
    }

    /// <summary>
    /// Inserts an effect at the specified index.
    /// </summary>
    public void Insert(int index, Effect effect)
    {
        if (index < 0 || index > _effects.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _effects.Insert(index, effect);
        Rebuild();
    }

    /// <summary>
    /// Removes an effect from the chain.
    /// </summary>
    public bool Remove(Effect effect)
    {
        int index = _effects.IndexOf(effect);
        if (index < 0) return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes the effect at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _effects.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var effect = _effects[index];
        _effects.RemoveAt(index);

        effect.Output.Disconnect();
        effect.Dispose();

        Rebuild();
    }

    /// <summary>
    /// Gets the effect at the specified index.
    /// </summary>
    public Effect this[int index]
    {
        get
        {
            if (index < 0 || index >= _effects.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _effects[index];
        }
    }

    /// <summary>
    /// Removes all effects from the chain.
    /// </summary>
    public void Clear()
    {
        if (_effects.Count == 0) return;

        foreach (var effect in _effects)
        {
            effect.Output.Disconnect();
            effect.Dispose();
        }

        _effects.Clear();
        Rebuild();
    }

    internal void UpdateEndpoints(AudioNode source, AudioNode destination)
    {
        _source = source;
        _destination = destination;
        Rebuild();
    }

    private void Rebuild()
    {
        _source.Disconnect();
        foreach (var effect in _effects)
        {
            effect.Output.Disconnect();
        }

        if (_effects.Count == 0)
        {
            _source.Connect(_destination);
        }
        else
        {
            _source.Connect(_effects[0].Input);

            for (int i = 0; i < _effects.Count - 1; i++)
            {
                _effects[i].Output.Connect(_effects[i + 1].Input);
            }

            _effects[^1].Output.Connect(_destination);
        }
    }

    public void Dispose()
    {
        Clear();
    }
}
