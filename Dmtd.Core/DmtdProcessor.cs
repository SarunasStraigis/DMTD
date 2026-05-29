using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace Dmtd.Core;

public sealed class DmtdProcessor
{
    private readonly int _sampleRate;
    private readonly double _beatFrequency;
    private readonly double _refFrequency;
    private readonly FreqEstimator _freqEstimator;
    private readonly DemodMode _demodMode;
    private readonly FreqSource _freqSource;
    private readonly double _iqLpfCutoffHz;
    private readonly int _iqLpfOrder;
    private readonly double _iqMinMag;
    private readonly IqWindow _iqWindow;
    private readonly double _pllKp;
    private readonly double _pllKi;
    private readonly double _pllMinMag;

    private double? _prevRawA;
    private double? _prevRawB;
    private double _unwrapOffsetA;
    private double _unwrapOffsetB;
    private double? _lastEstimatedFreq;
    private double? _pllPhaseA;
    private double? _pllPhaseB;
    private double? _pllFreqHz;
    private (int SampleRate, double Cutoff, int Order)? _lpfCacheKey;
    private double[][]? _lpfSos;

    public DmtdProcessor(DmtdSettings settings)
    {
        _sampleRate = settings.SampleRate;
        _beatFrequency = settings.BeatFrequency;
        _refFrequency = settings.RefFrequency;
        _freqEstimator = settings.FreqEstimator;
        _demodMode = settings.DemodMode;
        _freqSource = settings.FreqSource;
        _iqLpfCutoffHz = settings.IqLpfCutoffHz;
        _iqLpfOrder = settings.IqLpfOrder;
        _iqMinMag = settings.IqMinMag;
        _iqWindow = settings.IqWindow;
        _pllKp = settings.PllKp;
        _pllKi = settings.PllKi;
        _pllMinMag = settings.PllMinMag;
    }

    public void Reset()
    {
        _prevRawA = null;
        _prevRawB = null;
        _unwrapOffsetA = 0;
        _unwrapOffsetB = 0;
        _lastEstimatedFreq = null;
        _pllPhaseA = null;
        _pllPhaseB = null;
        _pllFreqHz = null;
    }

    public DspUnwrapState ExportUnwrapState(DmtdSettings settings) =>
        new()
        {
            SampleRate = settings.SampleRate,
            DemodMode = settings.DemodMode,
            FreqEstimator = settings.FreqEstimator,
            PrevRawA = _prevRawA,
            PrevRawB = _prevRawB,
            UnwrapOffsetA = _unwrapOffsetA,
            UnwrapOffsetB = _unwrapOffsetB,
            LastEstimatedFreq = _lastEstimatedFreq,
            PllPhaseA = _pllPhaseA,
            PllPhaseB = _pllPhaseB,
            PllFreqHz = _pllFreqHz
        };

    public void RestoreUnwrapState(DspUnwrapState state)
    {
        _prevRawA = state.PrevRawA;
        _prevRawB = state.PrevRawB;
        _unwrapOffsetA = state.UnwrapOffsetA;
        _unwrapOffsetB = state.UnwrapOffsetB;
        _lastEstimatedFreq = state.LastEstimatedFreq;
        _pllPhaseA = state.PllPhaseA;
        _pllPhaseB = state.PllPhaseB;
        _pllFreqHz = state.PllFreqHz;
    }

    public BlockProcessResult ProcessBlock(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB)
    {
        var n = Math.Min(chA.Length, chB.Length);
        if (n <= 0)
        {
            throw new ArgumentException("Empty block.");
        }

        var beatFreq = EstimateFrequency(chA, chB, n);

        double unwrappedA;
        double unwrappedB;
        switch (_demodMode)
        {
            case DemodMode.PllTracker:
                (unwrappedA, unwrappedB) = PllDemodulate(chA, chB, n, beatFreq);
                break;
            case DemodMode.BlockIqFir:
                (unwrappedA, unwrappedB) = BlockIqFirDemodulate(chA, chB, n, beatFreq);
                break;
            default:
                (unwrappedA, unwrappedB) = BlockIqDemodulate(chA, chB, n, beatFreq);
                break;
        }

        var phaseDiffRad = unwrappedA - unwrappedB;
        var psPerRad = PhaseMath.PsPerRad(_refFrequency);
        var phaseDiffPs = phaseDiffRad * psPerRad;

        var degPerRad = 180.0 / Math.PI;
        return new BlockProcessResult
        {
            PhaseDiffRad = phaseDiffRad,
            PhaseDiffPs = phaseDiffPs,
            BeatFreq = beatFreq,
            PhaseAPs = unwrappedA * psPerRad,
            PhaseBPs = unwrappedB * psPerRad,
            PhaseADeg = unwrappedA * degPerRad,
            PhaseBDeg = unwrappedB * degPerRad,
            RmsA = ComputeRms(chA[..n]),
            RmsB = ComputeRms(chB[..n])
        };
    }

    private (double A, double B) BlockIqDemodulate(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB, int n, double beatFreq)
    {
        var cosRef = new double[n];
        var sinRef = new double[n];
        FillReference(n, beatFreq, cosRef, sinRef);

        var iMixA = new double[n];
        var qMixA = new double[n];
        var iMixB = new double[n];
        var qMixB = new double[n];
        for (var i = 0; i < n; i++)
        {
            iMixA[i] = chA[i] * cosRef[i];
            qMixA[i] = chA[i] * sinRef[i];
            iMixB[i] = chB[i] * cosRef[i];
            qMixB[i] = chB[i] * sinRef[i];
        }

        var nEff = IntegerCycleN(n, beatFreq);
        var (iDcA, qDcA) = WindowedIqMean(iMixA, qMixA, nEff);
        var (iDcB, qDcB) = WindowedIqMean(iMixB, qMixB, nEff);

        var rawA = IqPhaseWithMagGate(iDcA, qDcA, ref _prevRawA, _iqMinMag);
        var rawB = IqPhaseWithMagGate(iDcB, qDcB, ref _prevRawB, _iqMinMag);
        return (UnwrapStep(rawA, ref _prevRawA, ref _unwrapOffsetA),
            UnwrapStep(rawB, ref _prevRawB, ref _unwrapOffsetB));
    }

    private (double A, double B) BlockIqFirDemodulate(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB, int n, double beatFreq)
    {
        var cosRef = new double[n];
        var sinRef = new double[n];
        FillReference(n, beatFreq, cosRef, sinRef);

        var iMixA = new double[n];
        var qMixA = new double[n];
        var iMixB = new double[n];
        var qMixB = new double[n];
        for (var i = 0; i < n; i++)
        {
            iMixA[i] = chA[i] * cosRef[i];
            qMixA[i] = chA[i] * sinRef[i];
            iMixB[i] = chB[i] * cosRef[i];
            qMixB[i] = chB[i] * sinRef[i];
        }

        double[] iA;
        double[] qA;
        double[] iB;
        double[] qB;
        try
        {
            var sos = GetLpfSos();
            iA = ButterworthFilter.SosFiltFilt(sos, iMixA);
            qA = ButterworthFilter.SosFiltFilt(sos, qMixA);
            iB = ButterworthFilter.SosFiltFilt(sos, iMixB);
            qB = ButterworthFilter.SosFiltFilt(sos, qMixB);
        }
        catch
        {
            iA = iMixA.ToArray();
            qA = qMixA.ToArray();
            iB = iMixB.ToArray();
            qB = qMixB.ToArray();
        }

        var trim = Math.Max(16, (int)(0.10 * n));
        if (2 * trim >= n)
        {
            trim = 0;
        }

        var iAT = trim > 0 ? iA.AsSpan(trim, n - 2 * trim).ToArray() : iA;
        var qAT = trim > 0 ? qA.AsSpan(trim, n - 2 * trim).ToArray() : qA;
        var iBT = trim > 0 ? iB.AsSpan(trim, n - 2 * trim).ToArray() : iB;
        var qBT = trim > 0 ? qB.AsSpan(trim, n - 2 * trim).ToArray() : qB;

        var nEff = IntegerCycleN(iAT.Length, beatFreq);
        var (iDcA, qDcA) = WindowedIqMean(iAT, qAT, nEff);
        var (iDcB, qDcB) = WindowedIqMean(iBT, qBT, nEff);

        var rawA = IqPhaseWithMagGate(iDcA, qDcA, ref _prevRawA, _iqMinMag);
        var rawB = IqPhaseWithMagGate(iDcB, qDcB, ref _prevRawB, _iqMinMag);
        return (UnwrapStep(rawA, ref _prevRawA, ref _unwrapOffsetA),
            UnwrapStep(rawB, ref _prevRawB, ref _unwrapOffsetB));
    }

    private (double A, double B) PllDemodulate(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB, int n, double beatFreqHint)
    {
        _pllPhaseA ??= 0;
        _pllPhaseB ??= 0;
        _pllFreqHz ??= beatFreqHint;

        var (phaseA, iA, qA) = PllChannelMeasure(chA, n, _pllPhaseA.Value, _pllFreqHz.Value);
        var (phaseB, iB, qB) = PllChannelMeasure(chB, n, _pllPhaseB.Value, _pllFreqHz.Value);

        var magA = Math.Sqrt(iA * iA + qA * qA);
        var magB = Math.Sqrt(iB * iB + qB * qB);
        var errA = Math.Atan2(qA, iA);
        var errB = Math.Atan2(qB, iB);
        var commonErr = 0.5 * (errA + errB);

        var blockDt = n / (double)_sampleRate;
        var pllFreqNext = _pllFreqHz.Value;
        var goodA = magA >= _pllMinMag;
        var goodB = magB >= _pllMinMag;
        if (goodA && goodB)
        {
            var avgMag = 0.5 * (magA + magB);
            var gain = avgMag / (avgMag + _pllMinMag);
            pllFreqNext += _pllKi * gain * (commonErr / (2.0 * Math.PI * blockDt));
        }

        pllFreqNext += 0.02 * (beatFreqHint - pllFreqNext);

        if (goodA)
        {
            var gainA = magA / (magA + _pllMinMag);
            phaseA += _pllKp * gainA * errA;
        }

        if (goodB)
        {
            var gainB = magB / (magB + _pllMinMag);
            phaseB += _pllKp * gainB * errB;
        }

        _pllPhaseA = phaseA;
        _pllPhaseB = phaseB;
        _pllFreqHz = pllFreqNext;
        return (phaseA, phaseB);
    }

    private (double Phase, double I, double Q) PllChannelMeasure(
        ReadOnlySpan<float> ch, int n, double phaseState, double sharedFreqHz)
    {
        var cosRef = new double[n];
        var sinRef = new double[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)_sampleRate;
            var ncoPhase = phaseState + 2.0 * Math.PI * sharedFreqHz * t;
            cosRef[i] = Math.Cos(ncoPhase);
            sinRef[i] = Math.Sin(ncoPhase);
        }

        var iMix = new double[n];
        var qMix = new double[n];
        for (var i = 0; i < n; i++)
        {
            iMix[i] = ch[i] * cosRef[i];
            qMix[i] = ch[i] * sinRef[i];
        }

        var nEff = IntegerCycleN(n, sharedFreqHz);
        var (iDc, qDc) = WindowedIqMean(iMix, qMix, nEff);
        var blockDt = n / (double)_sampleRate;
        var phasePropagated = phaseState + 2.0 * Math.PI * sharedFreqHz * blockDt;
        return (phasePropagated, iDc, qDc);
    }

    private void FillReference(int n, double beatFreq, double[] cosRef, double[] sinRef)
    {
        for (var i = 0; i < n; i++)
        {
            var phase = 2.0 * Math.PI * beatFreq * i / _sampleRate;
            cosRef[i] = Math.Cos(phase);
            sinRef[i] = Math.Sin(phase);
        }
    }

    private int IntegerCycleN(int n, double beatFreq)
    {
        if (beatFreq <= 0 || n <= 1 || _sampleRate <= 0)
        {
            return n;
        }

        var samplesPerCycle = _sampleRate / beatFreq;
        if (samplesPerCycle <= 0)
        {
            return n;
        }

        var cycles = (int)Math.Floor(n / samplesPerCycle);
        if (cycles < 1)
        {
            return n;
        }

        var nInt = (int)Math.Round(cycles * samplesPerCycle);
        return Math.Min(Math.Max(nInt, 1), n);
    }

    private (double I, double Q) WindowedIqMean(ReadOnlySpan<double> iArr, ReadOnlySpan<double> qArr, int nEff)
    {
        if (nEff <= 0 || iArr.Length == 0)
        {
            return (0, 0);
        }

        nEff = Math.Min(nEff, iArr.Length);
        if (_iqWindow == IqWindow.Hann && nEff >= 4)
        {
            double wSum = 0;
            double iSum = 0;
            double qSum = 0;
            for (var i = 0; i < nEff; i++)
            {
                var w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (nEff - 1));
                wSum += w;
                iSum += iArr[i] * w;
                qSum += qArr[i] * w;
            }

            if (wSum > 0)
            {
                return (iSum / wSum, qSum / wSum);
            }
        }

        double iMean = 0;
        double qMean = 0;
        for (var i = 0; i < nEff; i++)
        {
            iMean += iArr[i];
            qMean += qArr[i];
        }

        return (iMean / nEff, qMean / nEff);
    }

    private double[][] GetLpfSos()
    {
        var key = (_sampleRate, _iqLpfCutoffHz, _iqLpfOrder);
        if (_lpfSos is null || _lpfCacheKey != key)
        {
            _lpfSos = ButterworthFilter.DesignLowPassSos(_sampleRate, _iqLpfCutoffHz, _iqLpfOrder);
            _lpfCacheKey = key;
        }

        return _lpfSos;
    }

    private static double IqPhaseWithMagGate(double iDc, double qDc, ref double? prevRaw, double minMag)
    {
        var mag = Math.Sqrt(iDc * iDc + qDc * qDc);
        if (mag < minMag && prevRaw is not null)
        {
            return prevRaw.Value;
        }

        return Math.Atan2(qDc, iDc);
    }

    private static double UnwrapStep(double raw, ref double? prevRaw, ref double offset)
    {
        if (prevRaw is null)
        {
            prevRaw = raw;
            return raw + offset;
        }

        var jump = raw - prevRaw.Value;
        if (jump > Math.PI)
        {
            offset -= 2.0 * Math.PI;
        }
        else if (jump < -Math.PI)
        {
            offset += 2.0 * Math.PI;
        }

        prevRaw = raw;
        return raw + offset;
    }

    private double EstimateFrequency(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB, int n)
    {
        if (_freqEstimator == FreqEstimator.Fixed)
        {
            return _beatFrequency;
        }

        ReadOnlySpan<float> ch = _freqSource == FreqSource.ChA
            ? chA
            : AverageChannels(chA, chB, n);

        var (fftMag, freqs) = ComputeFftWithRate(ch, n);
        var nyquist = 0.5 * _sampleRate;

        (double Freq, double Mag)? PeakInWindow(double lo, double hi)
        {
            var loClamped = Math.Max(5.0, lo);
            var hiClamped = Math.Min(nyquist * 0.98, hi);
            if (hiClamped <= loClamped)
            {
                return null;
            }

            var bestMag = double.NegativeInfinity;
            var bestFreq = loClamped;
            var found = false;
            for (var i = 0; i < freqs.Length; i++)
            {
                if (freqs[i] >= loClamped && freqs[i] <= hiClamped && fftMag[i] > bestMag)
                {
                    bestMag = fftMag[i];
                    bestFreq = freqs[i];
                    found = true;
                }
            }

            return found ? (bestFreq, bestMag) : null;
        }

        var broad = PeakInWindow(20.0, Math.Min(nyquist * 0.98, 20_000.0));
        if (broad is null)
        {
            _lastEstimatedFreq = _beatFrequency;
            return _beatFrequency;
        }

        if (_lastEstimatedFreq is not null)
        {
            var guided = PeakInWindow(_lastEstimatedFreq.Value * 0.80, _lastEstimatedFreq.Value * 1.20);
            if (guided is not null)
            {
                _lastEstimatedFreq = guided.Value.Freq;
                return guided.Value.Freq;
            }

            _lastEstimatedFreq = broad.Value.Freq;
            return broad.Value.Freq;
        }

        var nominalPeak = PeakInWindow(_beatFrequency * 0.80, _beatFrequency * 1.20);
        var chosen = nominalPeak is not null && nominalPeak.Value.Mag >= 0.20 * broad.Value.Mag
            ? nominalPeak.Value.Freq
            : broad.Value.Freq;

        _lastEstimatedFreq = chosen;
        return chosen;
    }

    private static float[] AverageChannels(ReadOnlySpan<float> chA, ReadOnlySpan<float> chB, int n)
    {
        var avg = new float[n];
        for (var i = 0; i < n; i++)
        {
            avg[i] = 0.5f * (chA[i] + chB[i]);
        }

        return avg;
    }

    private (double[] Mag, double[] Freqs) ComputeFftWithRate(ReadOnlySpan<float> ch, int n)
    {
        var spectrumLength = n / 2 + 1;
        var buffer = new Complex[n];
        for (var i = 0; i < n; i++)
        {
            buffer[i] = new Complex(ch[i], 0);
        }

        Fourier.Forward(buffer, FourierOptions.Matlab);

        var mag = new double[spectrumLength];
        var freqs = new double[spectrumLength];
        for (var i = 0; i < spectrumLength; i++)
        {
            mag[i] = buffer[i].Magnitude;
            freqs[i] = i * _sampleRate / (double)n;
        }

        return (mag, freqs);
    }

    private static double ComputeRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sum = 0;
        foreach (var s in samples)
        {
            sum += s * s;
        }

        return Math.Sqrt(sum / samples.Length);
    }
}
