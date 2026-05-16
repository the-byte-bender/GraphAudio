using System;
using System.Runtime.CompilerServices;

namespace GraphAudio.Core;

internal class TwoStageConvolver : IDisposable
{
    private int _headBlockSize;
    private int _tailBlockSize;

    private PartitionedConvolver? _headConvolver;

    private PartitionedConvolver? _tailConvolver0;
    private float[]? _tailOutput0;
    private float[]? _tailPrecalculated0;

    private PartitionedConvolver? _tailConvolver;
    private float[]? _tailOutput;
    private float[]? _tailPrecalculated;
    private float[]? _backgroundProcessingInput;

    private float[]? _tailInput;
    private int _tailInputFill;
    private int _precalculatedPos;

    public TwoStageConvolver() { }

    public void Init(
        int headBlockSize,
        int tailBlockSize,
        ReadOnlySpan<float> ir,
        bool normalize = true
    )
    {
        Reset();

        if (headBlockSize <= 0 || tailBlockSize <= 0)
            throw new ArgumentOutOfRangeException("Block sizes must be positive.");

        if (headBlockSize > tailBlockSize)
            (headBlockSize, tailBlockSize) = (tailBlockSize, headBlockSize);

        int irLen = ir.Length;
        while (irLen > 0 && MathF.Abs(ir[irLen - 1]) < 00.000001f)
            irLen--;

        if (irLen == 0)
            return;

        _headBlockSize = NextPowerOfTwo(headBlockSize);
        _tailBlockSize = NextPowerOfTwo(tailBlockSize);

        int headIrLen = Math.Min(irLen, _tailBlockSize);
        float? scale = normalize
            ? PartitionedConvolver.CalculateNormalizationScale(ir[..irLen])
            : null;
        _headConvolver = new PartitionedConvolver(
            ir[..headIrLen],
            _headBlockSize,
            normalize,
            scale
        );

        if (irLen > _tailBlockSize)
        {
            int tail0IrLen = Math.Min(irLen - _tailBlockSize, _tailBlockSize);
            _tailConvolver0 = new PartitionedConvolver(
                ir.Slice(_tailBlockSize, tail0IrLen),
                _headBlockSize,
                normalize,
                scale
            );
            _tailOutput0 = new float[_tailBlockSize];
            _tailPrecalculated0 = new float[_tailBlockSize];
        }

        if (irLen > 2 * _tailBlockSize)
        {
            _tailConvolver = new PartitionedConvolver(
                ir[(2 * _tailBlockSize)..irLen],
                _tailBlockSize,
                normalize,
                scale
            );
            _tailOutput = new float[_tailBlockSize];
            _tailPrecalculated = new float[_tailBlockSize];
            _backgroundProcessingInput = new float[_tailBlockSize];
        }

        if (_tailConvolver0 is not null || _tailConvolver is not null)
            _tailInput = new float[_tailBlockSize];

        _tailInputFill = 0;
        _precalculatedPos = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        int len = input.Length;

        _headConvolver!.Process(input, output);

        if (_tailInput is null)
            return;

        int processed = 0;
        while (processed < len)
        {
            int remaining = len - processed;
            int processing = Math.Min(
                remaining,
                _headBlockSize - (_tailInputFill % _headBlockSize)
            );

            int sumBegin = processed;
            int sumEnd = processed + processing;
            int precalcPos = _precalculatedPos;

            if (_tailPrecalculated0 is not null)
            {
                ref float t0Ref = ref _tailPrecalculated0[precalcPos];
                for (int i = sumBegin; i < sumEnd; i++)
                {
                    output[i] += t0Ref;
                    t0Ref = ref Unsafe.Add(ref t0Ref, 1);
                }
            }

            if (_tailPrecalculated is not null)
            {
                ref float tRef = ref _tailPrecalculated[precalcPos];
                for (int i = sumBegin; i < sumEnd; i++)
                {
                    output[i] += tRef;
                    tRef = ref Unsafe.Add(ref tRef, 1);
                }
            }

            _precalculatedPos += processing;

            input.Slice(processed, processing).CopyTo(_tailInput.AsSpan(_tailInputFill));
            _tailInputFill += processing;

            if (
                _tailConvolver0 is not null
                && _tailOutput0 is not null
                && _tailPrecalculated0 is not null
                && _tailInputFill % _headBlockSize == 0
            )
            {
                int blockOffset = _tailInputFill - _headBlockSize;
                _tailConvolver0.Process(
                    _tailInput.AsSpan(blockOffset, _headBlockSize),
                    _tailOutput0.AsSpan(blockOffset, _headBlockSize)
                );

                if (_tailInputFill == _tailBlockSize)
                    Swap(ref _tailPrecalculated0!, ref _tailOutput0!);
            }

            if (
                _tailConvolver is not null
                && _tailOutput is not null
                && _tailPrecalculated is not null
                && _backgroundProcessingInput is not null
                && _tailInputFill == _tailBlockSize
            )
            {
                WaitForBackgroundProcessing();
                Swap(ref _tailPrecalculated!, ref _tailOutput!);
                _tailInput.AsSpan().CopyTo(_backgroundProcessingInput);
                StartBackgroundProcessing();
            }

            if (_tailInputFill == _tailBlockSize)
            {
                _tailInputFill = 0;
                _precalculatedPos = 0;
            }

            processed += processing;
        }
    }

    protected virtual void StartBackgroundProcessing()
    {
        DoBackgroundProcessing();
    }

    protected virtual void WaitForBackgroundProcessing() { }

    protected void DoBackgroundProcessing()
    {
        if (
            _tailConvolver is not null
            && _backgroundProcessingInput is not null
            && _tailOutput is not null
        )
            _tailConvolver.Process(_backgroundProcessingInput, _tailOutput);
    }

    public void Reset()
    {
        _headBlockSize = 0;
        _tailBlockSize = 0;
        _headConvolver = null;
        _tailConvolver0 = null;
        _tailOutput0 = null;
        _tailPrecalculated0 = null;
        _tailConvolver = null;
        _tailOutput = null;
        _tailPrecalculated = null;
        _backgroundProcessingInput = null;
        _tailInput = null;
        _tailInputFill = 0;
        _precalculatedPos = 0;
    }

    public virtual void Dispose() => Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(ref float[] a, ref float[] b) => (a, b) = (b, a);

    private static int NextPowerOfTwo(int v)
    {
        if (v <= 1)
            return 1;
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
