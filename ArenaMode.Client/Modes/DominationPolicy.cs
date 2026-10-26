using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>One sample of physical, eligible occupants; counts never accelerate capture.</summary>
internal readonly struct DominationOccupancy
{
    internal DominationOccupancy(int usec, int bear)
    {
        if (usec < 0) throw new ArgumentOutOfRangeException(nameof(usec));
        if (bear < 0) throw new ArgumentOutOfRangeException(nameof(bear));
        Usec = usec; Bear = bear;
    }

    internal int Usec { get; }
    internal int Bear { get; }
}

/// <summary>Immutable read model: USEC is positive, BEAR negative, zero is neutral.</summary>
internal readonly struct DominationPointState
{
    internal DominationPointState(bool? ownerUsec, float captureProgress, DominationOccupancy occupancy)
    {
        OwnerUsec = ownerUsec; CaptureProgress = captureProgress;
        UsecOccupants = occupancy.Usec; BearOccupants = occupancy.Bear;
    }

    internal bool? OwnerUsec { get; }
    internal float CaptureProgress { get; }
    internal int UsecOccupants { get; }
    internal int BearOccupants { get; }
    internal bool Contested => UsecOccupants > 0 && BearOccupants > 0;
    internal bool UnderAttack => OwnerUsec.HasValue
        && (OwnerUsec.Value ? BearOccupants > 0 : UsecOccupants > 0);
}

/// <summary>
/// Capture and holding income only. The exact raid owner supplies eligible
/// occupants and positive simulation-time steps while live, clips the final
/// step at the match deadline and owns win/exit decisions separately.
/// A neutral point takes CaptureSeconds; an enemy point first neutralizes over
/// CaptureSeconds, then takes the same time to capture. Empty points retain
/// partial progress/ownership. Contested or attacked points pay no income.
/// </summary>
internal sealed class DominationPolicy
{
    internal const float MaxStepSeconds = 30f;
    // Float deltaTime partitioning can leave a nominal second a few nanoseconds
    // short. This tolerance is only numerical; it is never a catch-up interval.
    private const double RoundingTolerance = 0.000001d;
    private struct Point { internal double Progress; internal bool? OwnerUsec; }
    private readonly Point[] _points;
    private readonly DominationPointState[] _views;
    private readonly DominationOccupancy[] _sample;
    private readonly double[] _usecIncomeStarts, _bearIncomeStarts;
    private readonly double _captureSeconds;
    private double _usecSeconds, _bearSeconds;

    internal DominationPolicy(int pointCount = 3, float captureSeconds = 5f)
    {
        if (pointCount < 1 || pointCount > 16) throw new ArgumentOutOfRangeException(nameof(pointCount));
        if (!Finite(captureSeconds) || captureSeconds < .1f || captureSeconds > 60f)
            throw new ArgumentOutOfRangeException(nameof(captureSeconds));
        _captureSeconds = captureSeconds;
        _points = new Point[pointCount];
        _views = new DominationPointState[pointCount];
        _sample = new DominationOccupancy[pointCount];
        _usecIncomeStarts = new double[pointCount];
        _bearIncomeStarts = new double[pointCount];
        Points = Array.AsReadOnly(_views);
    }

    internal IReadOnlyList<DominationPointState> Points { get; }
    internal int UsecScore => WholeScore(_usecSeconds);
    internal int BearScore => WholeScore(_bearSeconds);

    internal void Advance(float deltaSeconds, IReadOnlyList<DominationOccupancy> occupancy)
        => Advance((double)deltaSeconds, occupancy);

    // Timestamp differences retain their double precision through final clipping.
    internal void Advance(double deltaSeconds, IReadOnlyList<DominationOccupancy> occupancy)
    {
        ReadSample(deltaSeconds, occupancy);
        AdvancePrepared(deltaSeconds);
    }

    /// <summary>
    /// Apply only the prefix ending when either faction first reaches the
    /// score limit. Income is piecewise linear: each point starts contributing
    /// now or at its actual capture completion. Return the applied duration;
    /// the lifecycle owns the resulting absolute finish time and result.
    /// </summary>
    internal double AdvanceUntilScoreLimit(double deltaSeconds,
        IReadOnlyList<DominationOccupancy> occupancy, int scoreLimit)
    {
        if (scoreLimit < 1) throw new ArgumentOutOfRangeException(nameof(scoreLimit));
        ReadSample(deltaSeconds, occupancy);
        int usecCount = 0, bearCount = 0;
        for (int i = 0; i < _points.Length; i++)
        {
            var point = _points[i];
            var current = _sample[i];
            if (current.Usec > 0 && current.Bear > 0) continue;
            bool empty = current.Usec == 0 && current.Bear == 0;
            if (empty && !point.OwnerUsec.HasValue) continue;
            bool usec = empty ? point.OwnerUsec.Value : current.Usec > 0;
            double direction = usec ? 1d : -1d;
            double begins = empty || point.OwnerUsec == usec ? 0d
                : Math.Max(0d, (direction - point.Progress) * direction * _captureSeconds);
            if (begins > deltaSeconds) continue;
            if (usec) _usecIncomeStarts[usecCount++] = begins;
            else _bearIncomeStarts[bearCount++] = begins;
        }
        double usecCrossing = Crossing(scoreLimit - _usecSeconds, _usecIncomeStarts, usecCount);
        double bearCrossing = Crossing(scoreLimit - _bearSeconds, _bearIncomeStarts, bearCount);
        double duration = Math.Min(deltaSeconds, Math.Min(usecCrossing, bearCrossing));
        AdvancePrepared(duration);
        return duration;
    }

    private static double Crossing(double remaining, double[] starts, int count)
    {
        if (remaining <= RoundingTolerance) return 0d;
        Array.Sort(starts, 0, count);
        double at = 0d;
        int rate = 0;
        for (int i = 0; i < count; i++)
        {
            double intervalIncome = (starts[i] - at) * rate;
            if (rate > 0 && remaining <= intervalIncome)
                return at + remaining / rate;
            remaining -= intervalIncome;
            at = starts[i];
            rate++;
        }
        return rate == 0 ? double.PositiveInfinity : at + remaining / rate;
    }

    private void ReadSample(double deltaSeconds, IReadOnlyList<DominationOccupancy> occupancy)
    {
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d || deltaSeconds > MaxStepSeconds)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (occupancy == null) throw new ArgumentNullException(nameof(occupancy));
        if (occupancy.Count != _points.Length) throw new ArgumentException("One occupancy per point is required.", nameof(occupancy));
        // Read and validate the complete sample before changing visible state.
        // A caller-owned list throwing halfway through cannot partially score.
        for (int i = 0; i < _points.Length; i++)
        {
            var current = occupancy[i];
            if (current.Usec < 0 || current.Bear < 0) throw new ArgumentException("Occupancy must be nonnegative.", nameof(occupancy));
            _sample[i] = current;
        }
    }

    private void AdvancePrepared(double deltaSeconds)
    {
        double usecIncome = 0d, bearIncome = 0d;
        for (int i = 0; i < _points.Length; i++)
        {
            var point = _points[i];
            var current = _sample[i];
            bool contested = current.Usec > 0 && current.Bear > 0;
            bool empty = current.Usec == 0 && current.Bear == 0;
            if (!contested)
            {
                if (empty)
                {
                    if (point.OwnerUsec.HasValue)
                        Income(point.OwnerUsec.Value, deltaSeconds, ref usecIncome, ref bearIncome);
                }
                else
                {
                    bool usec = current.Usec > 0;
                    double direction = usec ? 1d : -1d;
                    bool ownedAtStart = point.OwnerUsec == usec;
                    double captureIn = Math.Max(0d, (direction - point.Progress) * direction * _captureSeconds);
                    point.Progress = Math.Max(-1d, Math.Min(1d, point.Progress + direction * deltaSeconds / _captureSeconds));
                    if (point.OwnerUsec.HasValue && point.OwnerUsec.Value != usec && point.Progress * direction >= -RoundingTolerance)
                        point.OwnerUsec = null;
                    if (deltaSeconds + RoundingTolerance >= captureIn)
                    {
                        point.Progress = direction;
                        point.OwnerUsec = usec;
                    }
                    // Owned/unopposed from the start earns the whole interval.
                    // Otherwise only the time after actual capture belongs to
                    // the new owner; neutralization/capture time never pays.
                    Income(usec, ownedAtStart ? deltaSeconds : Math.Max(0d, deltaSeconds - captureIn),
                        ref usecIncome, ref bearIncome);
                }
            }
            _points[i] = point;
            _views[i] = new DominationPointState(point.OwnerUsec, (float)point.Progress, current);
        }
        _usecSeconds = Math.Min(int.MaxValue, _usecSeconds + usecIncome);
        _bearSeconds = Math.Min(int.MaxValue, _bearSeconds + bearIncome);
    }

    private static void Income(bool usec, double seconds, ref double usecIncome, ref double bearIncome)
    {
        if (usec) usecIncome += seconds; else bearIncome += seconds;
    }

    private static int WholeScore(double seconds)
        => (int)Math.Min(int.MaxValue, Math.Floor(seconds + RoundingTolerance));

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
