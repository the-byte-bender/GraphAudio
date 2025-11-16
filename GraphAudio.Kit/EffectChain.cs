using System;
using System.Collections.Generic;
using GraphAudio.Core;
using GraphAudio.Nodes;

namespace GraphAudio.Kit;

/// <summary>
/// Manages a chain of audio effects.
/// </summary>
/// <remarks>
/// This will not take ownership of any nodes/effects added to the chain. The caller is responsible for disposing them if needed.
/// </remarks>
public sealed class EffectChain
{
    private readonly List<AudioNode> _effects = new();
    private readonly AudioContextBase _context;
    private AudioNode _source;
    private AudioNode _destination;

    /// <summary>
    /// The effects in the chain, in processing order.
    /// </summary>
    public IReadOnlyList<AudioNode> Effects => _effects;

    /// <summary>
    /// Gets the number of effects in the chain.
    /// </summary>
    public int Count => _effects.Count;

    internal EffectChain(AudioContextBase context, AudioNode source, AudioNode destination)
    {
        _context = context;
        _source = source;
        _destination = destination;
        _source.Connect(_destination);
    }

    /// <summary>
    /// Adds an effect to the end of the chain.
    /// </summary>
    public void Add(AudioNode effect)
    {
        Insert(_effects.Count, effect);
    }

    /// <summary>
    /// Inserts an effect at the specified index.
    /// </summary>
    public void Insert(int index, AudioNode effect)
    {
        if (index < 0 || index > _effects.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _effects.Insert(index, effect);
        Rebuild();
    }

    /// <summary>
    /// Removes an effect from the chain.
    /// </summary>
    public bool Remove(AudioNode effect)
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

        _effects.RemoveAt(index);
        Rebuild();
    }

    /// <summary>
    /// Gets the effect at the specified index.
    /// </summary>
    public AudioNode this[int index]
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
            effect.Disconnect();
        }

        if (_effects.Count == 0)
        {
            _source.Connect(_destination);
        }
        else
        {
            _source.Connect(_effects[0]);

            for (int i = 0; i < _effects.Count - 1; i++)
            {
                _effects[i].Connect(_effects[i + 1]);
            }

            _effects[^1].Connect(_destination);
        }
    }
}
