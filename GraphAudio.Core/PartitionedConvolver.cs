// Adapted to C# from LabSound: https://github.com/LabSound/LabSound

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using FftFlat;

namespace GraphAudio.Core;

internal unsafe class PartitionedConvolver
{
    private readonly int _blockSize;
    private readonly int _fftSize;
    private readonly int _complexCount;
    private readonly int _nPartitions;
    private readonly int _mask;

    private float[] _irReal;
    private float[] _irImag;

    private float[] _delayReal;
    private float[] _delayImag;
    private int _writeIndex;

    private double[] _fftInput;
    private float[] _overlap;
    private float[] _tempReal;
    private float[] _tempImag;
    private float[] _accReal;
    private float[] _accImag;
    private readonly Complex[] _fftOutputComplex;

    private readonly RealFourierTransform _fft;

    public PartitionedConvolver(ReadOnlySpan<float> impulseResponse, int blockSize = 128, bool normalize = true)
    {
        _blockSize = blockSize;
        _fftSize = blockSize * 2;
        _complexCount = (_fftSize / 2) + 1;
        _fft = new RealFourierTransform(_fftSize);

        _nPartitions = (int)Math.Ceiling((double)impulseResponse.Length / blockSize);

        int totalFloats = _nPartitions * _complexCount;

        _irReal = new float[totalFloats];
        _irImag = new float[totalFloats];
        _delayReal = new float[totalFloats];
        _delayImag = new float[totalFloats];

        _fftInput = new double[_fftSize + 2];
        _fftOutputComplex = new Complex[_complexCount];
        _overlap = new float[_blockSize];

        _tempReal = new float[_complexCount];
        _tempImag = new float[_complexCount];
        _accReal = new float[_complexCount];
        _accImag = new float[_complexCount];

        PrepareImpulseResponse(impulseResponse, normalize);
    }

    private void PrepareImpulseResponse(ReadOnlySpan<float> sourceIr, bool normalize)
    {
        float scale = 1.0f;
        if (normalize) scale = CalculateNormalizationScale(sourceIr);

        double[] tempTime = new double[_fftSize + 2];
        int irLen = sourceIr.Length;

        for (int p = 0; p < _nPartitions; p++)
        {
            Array.Clear(tempTime, 0, tempTime.Length);
            int offset = p * _blockSize;
            int len = Math.Min(_blockSize, irLen - offset);

            for (int i = 0; i < len; i++)
                tempTime[i] = sourceIr[offset + i] * scale;

            Span<Complex> result = _fft.Forward(tempTime);

            int partitionOffset = p * _complexCount;
            for (int i = 0; i < _complexCount; i++)
            {
                _irReal[partitionOffset + i] = (float)result[i].Real;
                _irImag[partitionOffset + i] = (float)result[i].Imaginary;
            }
        }
    }

    private static float CalculateNormalizationScale(ReadOnlySpan<float> response)
    {
        const float GainCalibration = -58;
        const float MinPower = 0.000125f;
        double sumSquared = 0;
        for (int i = 0; i < response.Length; i++) sumSquared += response[i] * response[i];
        float power = (float)Math.Sqrt(sumSquared / response.Length);
        if (float.IsNaN(power) || float.IsInfinity(power) || power < MinPower) power = MinPower;
        return (1.0f / power) * (float)Math.Pow(10, GainCalibration * 0.05f);
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        for (int i = 0; i < _blockSize; i++) _fftInput[i] = input[i];
        Array.Clear(_fftInput, _blockSize, _blockSize);

        _fft.Forward(_fftInput).CopyTo(_fftOutputComplex);

        fixed (Complex* pComplex = _fftOutputComplex)
        fixed (float* pReal = _tempReal)
        fixed (float* pImag = _tempImag)
        {
            for (int i = 0; i < _complexCount; i++)
            {
                pReal[i] = (float)pComplex[i].Real;
                pImag[i] = (float)pComplex[i].Imaginary;
            }
        }

        int currentOffset = _writeIndex * _complexCount;
        Array.Copy(_tempReal, 0, _delayReal, currentOffset, _complexCount);
        Array.Copy(_tempImag, 0, _delayImag, currentOffset, _complexCount);
        ProcessSpectralConvolution();

        _writeIndex--;
        if (_writeIndex < 0) _writeIndex = _nPartitions - 1;

        fixed (float* pAccR = _accReal)
        fixed (float* pAccI = _accImag)
        fixed (Complex* pOut = _fftOutputComplex)
        {
            for (int i = 0; i < _complexCount; i++)
            {
                pOut[i] = new Complex(pAccR[i], pAccI[i]);
            }
        }

        Span<double> resultSpan = _fft.Inverse(_fftOutputComplex);

        fixed (double* pResult = resultSpan)
        fixed (float* pOverlap = _overlap)
        fixed (float* pOut = output)
        {
            for (int i = 0; i < _blockSize; i++)
            {
                pOut[i] = (float)pResult[i] + pOverlap[i];
                pOverlap[i] = (float)pResult[i + _blockSize];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessSpectralConvolution()
    {
        Array.Clear(_accReal, 0, _complexCount);
        Array.Clear(_accImag, 0, _complexCount);

        int count = _complexCount;
        int partitions = _nPartitions;
        int currentDelayIdx = _writeIndex;

        fixed (float* pAccR = _accReal)
        fixed (float* pAccI = _accImag)
        fixed (float* pDelayRBase = _delayReal)
        fixed (float* pDelayIBase = _delayImag)
        fixed (float* pIrRBase = _irReal)
        fixed (float* pIrIBase = _irImag)
        {
            for (int p = 0; p < partitions; p++)
            {
                int delayPos = (currentDelayIdx + p);
                if (delayPos >= partitions) delayPos -= partitions;

                float* pDr = pDelayRBase + (delayPos * count);
                float* pDi = pDelayIBase + (delayPos * count);
                float* pIr = pIrRBase + (p * count);
                float* pIi = pIrIBase + (p * count);

                int i = 0;
                if (Avx.IsSupported)
                {
                    int avxCount = count - 8;
                    for (; i <= avxCount; i += 8)
                    {
                        Vector256<float> vr_d = Avx.LoadVector256(pDr + i);
                        Vector256<float> vi_d = Avx.LoadVector256(pDi + i);
                        Vector256<float> vr_ir = Avx.LoadVector256(pIr + i);
                        Vector256<float> vi_ir = Avx.LoadVector256(pIi + i);

                        Vector256<float> vacc_r = Avx.LoadVector256(pAccR + i);
                        Vector256<float> vacc_i = Avx.LoadVector256(pAccI + i);

                        Vector256<float> ac = Avx.Multiply(vr_d, vr_ir);
                        Vector256<float> bd = Avx.Multiply(vi_d, vi_ir);
                        Vector256<float> real_res = Avx.Subtract(ac, bd);

                        Vector256<float> ad = Avx.Multiply(vr_d, vi_ir);
                        Vector256<float> bc = Avx.Multiply(vi_d, vr_ir);
                        Vector256<float> imag_res = Avx.Add(ad, bc);

                        vacc_r = Avx.Add(vacc_r, real_res);
                        vacc_i = Avx.Add(vacc_i, imag_res);

                        Avx.Store(pAccR + i, vacc_r);
                        Avx.Store(pAccI + i, vacc_i);
                    }
                }

                for (; i < count; i++)
                {
                    float dr = pDr[i];
                    float di = pDi[i];
                    float ir = pIr[i];
                    float ii = pIi[i];

                    pAccR[i] += (dr * ir) - (di * ii);
                    pAccI[i] += (dr * ii) + (di * ir);
                }
            }
        }
    }
}
