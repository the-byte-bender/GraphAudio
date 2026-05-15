using System;
using System.Runtime.CompilerServices;

namespace GraphAudio.Core;

internal class TwoStageConvolver : IDisposable
{
    private int _headBlockSize;
    private int _tailBlockSize;

    private PartitionedConvolver? _headConvolver;

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

        // Trim trailing near-silence
        int irLen = ir.Length;
        while (irLen > 0 && MathF.Abs(ir[irLen - 1]) < 0.000001f)
            irLen--;

        if (irLen == 0)
            return;

        _headBlockSize = NextPowerOfTwo(headBlockSize);
        _tailBlockSize = NextPowerOfTwo(tailBlockSize);

        int headIrLen = Math.Min(irLen, _tailBlockSize);
        _headConvolver = new PartitionedConvolver(ir[..headIrLen], _headBlockSize, normalize);

        if (irLen > _tailBlockSize)
        {
            _tailConvolver = new PartitionedConvolver(
                ir[_tailBlockSize..irLen],
                _tailBlockSize,
                normalize
            );
            _tailOutput = new float[_tailBlockSize];
            _tailPrecalculated = new float[_tailBlockSize];
            _backgroundProcessingInput = new float[_tailBlockSize];
            _tailInput = new float[_tailBlockSize];
        }

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

            if (_tailPrecalculated is not null)
            {
                int precalcPos = _precalculatedPos;
                int sumEnd = processed + processing;
                ref float tailRef = ref _tailPrecalculated[precalcPos];
                for (int i = processed; i < sumEnd; i++)
                {
                    output[i] += tailRef;
                    tailRef = ref Unsafe.Add(ref tailRef, 1);
                }
            }

            _precalculatedPos += processing;

            input.Slice(processed, processing).CopyTo(_tailInput.AsSpan(_tailInputFill));
            _tailInputFill += processing;

            if (_tailInputFill == _tailBlockSize)
            {
                WaitForBackgroundProcessing();
                Swap(ref _tailPrecalculated!, ref _tailOutput!);
                _tailInput.AsSpan().CopyTo(_backgroundProcessingInput);
                StartBackgroundProcessing();

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
