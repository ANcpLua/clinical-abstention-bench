namespace ClinicalAbstentionBench;

/// A rate, kept together with the counts it came from — because 100 % of 12 and 100 % of 1,200 are
/// not the same claim, and a bare double loses the difference.
public readonly record struct Rate(int Successes, int Total)
{
    public double Value => Total == 0 ? 0 : (double)Successes / Total;

    /// 95 % Wilson score interval.
    public double Lower => Wilson.Interval(Successes, Total).Lower;

    public double Upper => Wilson.Interval(Successes, Total).Upper;

    /// A rate reads as a point estimate wherever it appears as a number, so the Gate and every
    /// existing comparison keep working — but the interval is always one property away.
    public static implicit operator double(Rate r) => r.Value;

    /// e.g. "100 [76–100]" — percentages, with the 95 % interval in brackets.
    public override string ToString()
        => Total == 0
            ? "n/a"
            : $"{Pct(Value)} [{Pct(Lower)}–{Pct(Upper)}]";

    private static string Pct(double v) => Math.Round(v * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

/// The Wilson score interval for a binomial proportion.
///
/// The benchmark has twelve must-abstain items. On that n, a 0 % unsupported-answer rate and a 17 %
/// one are not distinguishable, and the normal-approximation ("Wald") interval — the one everybody
/// reaches for — degenerates to zero width at exactly the rates this benchmark reports most often,
/// 0 % and 100 %. It would print "100 % ± 0", which is a lie. Wilson does not have that failure: it
/// stays inside [0, 1] and keeps sensible width at the boundaries.
///
///     centre    = (p̂ + z²/2n) / (1 + z²/n)
///     half-width = z/(1 + z²/n) · √( p̂(1−p̂)/n + z²/4n² )
///
/// Wilson, E. B. (1927), "Probable inference, the law of succession, and statistical inference",
/// JASA 22(158), 209–212.
public static class Wilson
{
    /// z for a two-sided 95 % interval.
    public const double Z95 = 1.959963984540054;

    public static (double Lower, double Upper) Interval(int successes, int total, double z = Z95)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "total cannot be negative.");
        if (successes < 0 || successes > total)
            throw new ArgumentOutOfRangeException(nameof(successes), $"successes ({successes}) must be within [0, {total}].");

        // No observations means no information — the honest interval is the whole range.
        if (total == 0) return (0.0, 1.0);

        var n = (double)total;
        var p = successes / n;
        var z2 = z * z;

        var denominator = 1 + z2 / n;
        var centre = (p + z2 / (2 * n)) / denominator;
        var halfWidth = z / denominator * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n));

        var lower = Math.Clamp(centre - halfWidth, 0.0, 1.0);
        var upper = Math.Clamp(centre + halfWidth, 0.0, 1.0);

        // At p̂ = 0 the centre and the half-width are analytically equal, so the lower bound is exactly
        // zero; the mirror holds at p̂ = 1. In floating point the cancellation is not quite exact and
        // leaves ~1e-17 behind, which would put the point estimate marginally OUTSIDE its own
        // interval. Pin the two boundaries to the values the algebra gives.
        if (successes == 0) lower = 0.0;
        if (successes == total) upper = 1.0;

        return (lower, upper);
    }
}
