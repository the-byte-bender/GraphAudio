using System;
using System.Runtime.CompilerServices;

namespace GraphAudio.Core;

public readonly ref struct ResampleResult
{
    public readonly int InputConsumed;
    public readonly int OutputProduced;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResampleResult(int inputConsumed, int outputProduced)
    {
        InputConsumed = inputConsumed;
        OutputProduced = outputProduced;
    }
}

public struct CubicResampler
{
    public float S0, S1, S2, S3;
    public double Pos;
    public byte Ready;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResampleResult Process(ReadOnlySpan<float> input, Span<float> output, double rate)
    {
        int inPos = 0;
        int outPos = 0;

        while (Ready < 4 && inPos < input.Length)
        {
            Shift(input[inPos++]);
            Ready++;
        }

        if (Ready < 4)
            return new ResampleResult(inPos, outPos);

        while (outPos < output.Length)
        {
            int consume = (int)Pos;
            if (inPos + consume > input.Length)
                break;

            for (int i = 0; i < consume; i++)
                Shift(input[inPos++]);

            Pos -= consume;

            float t = (float)Pos;
            output[outPos++] = S1 + t * (
                0.5f * (S2 - S0) + t * (
                    (S0 - 2.5f * S1 + 2f * S2 - 0.5f * S3) + t *
                    (0.5f * (S3 - S0) + 1.5f * (S1 - S2))
                )
            );

            Pos += rate;
        }

        return new ResampleResult(inPos, outPos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        S0 = S1 = S2 = S3 = 0;
        Pos = 0;
        Ready = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetupLoop(float end2, float end1, float start1, float start2)
    {
        S0 = end2;
        S1 = end1;
        S2 = start1;
        S3 = start2;
        Pos = 0;
        Ready = 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int InputNeeded(int outputCount, double rate)
    {
        return (int)Math.Ceiling(outputCount * rate + Pos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Shift(float sample)
    {
        S0 = S1;
        S1 = S2;
        S2 = S3;
        S3 = sample;
    }
}
