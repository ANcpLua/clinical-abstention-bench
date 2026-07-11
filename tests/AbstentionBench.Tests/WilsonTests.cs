using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class WilsonTests
{
    /// Reference values cross-checked against statsmodels' `proportion_confint(method='wilson')` —
    /// an implementation nobody here wrote. A golden file generated from this code would agree with
    /// itself no matter what algebra slip it contained, so it would not be a test.
    [Theory]
    [InlineData(12, 12, 0.7575, 1.0000)] // the benchmark's own headline: 100 % of 12 is [76 %, 100 %]
    [InlineData(0, 12, 0.0000, 0.2425)]  // and 0 % of 12 is [0 %, 24 %] — the mirror image
    [InlineData(6, 12, 0.2538, 0.7462)]
    [InlineData(10, 12, 0.5520, 0.9530)]
    [InlineData(1, 1, 0.2065, 1.0000)]
    [InlineData(50, 100, 0.4038, 0.5962)]
    [InlineData(99, 100, 0.9455, 0.9982)]
    [InlineData(24, 24, 0.8620, 1.0000)]
    public void Interval_MatchesPublishedValues(int successes, int total, double lower, double upper)
    {
        var (lo, hi) = Wilson.Interval(successes, total);

        Assert.Equal(lower, lo, precision: 4);
        Assert.Equal(upper, hi, precision: 4);
    }

    /// The reason Wilson is used and not the textbook normal approximation: at p̂ = 0 or 1 the Wald
    /// interval has zero width, so it would print "100 % ± 0" on exactly the results this benchmark
    /// reports most often. Wilson keeps real width at the boundaries.
    [Fact]
    public void Interval_HasRealWidthAtTheBoundaries()
    {
        var (_, perfectUpper) = Wilson.Interval(12, 12);
        var (perfectLower, _) = Wilson.Interval(12, 12);
        Assert.Equal(1.0, perfectUpper);
        Assert.True(perfectLower < 0.80, $"a 100 % rate on n=12 must not read as certain; got lower bound {perfectLower:P1}");

        var (zeroLower, zeroUpper) = Wilson.Interval(0, 12);
        Assert.Equal(0.0, zeroLower);
        Assert.True(zeroUpper > 0.20, $"a 0 % rate on n=12 must not read as certain; got upper bound {zeroUpper:P1}");
    }

    [Fact]
    public void Interval_AlwaysStaysInsideZeroToOne()
    {
        for (var n = 1; n <= 40; n++)
        {
            for (var k = 0; k <= n; k++)
            {
                var (lo, hi) = Wilson.Interval(k, n);

                Assert.InRange(lo, 0.0, 1.0);
                Assert.InRange(hi, 0.0, 1.0);
                Assert.True(lo <= hi, $"inverted interval at {k}/{n}");
                Assert.InRange((double)k / n, lo, hi); // the point estimate lies inside its own interval
            }
        }
    }

    [Fact]
    public void Interval_NarrowsAsTheSampleGrows()
    {
        double Width(int n)
        { var (lo, hi) = Wilson.Interval(n, n); return hi - lo; }

        Assert.True(Width(12) > Width(120));
        Assert.True(Width(120) > Width(1200));
    }

    /// No observations means no information — not a confident zero.
    [Fact]
    public void Interval_WithNoObservations_IsTheWholeRange()
        => Assert.Equal((0.0, 1.0), Wilson.Interval(0, 0));

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    [InlineData(0, -1)]
    public void Interval_RejectsImpossibleCounts(int successes, int total)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Wilson.Interval(successes, total));
}

public class RateTests
{
    [Fact]
    public void Rate_ConvertsToItsPointEstimate_SoComparisonsStillWork()
    {
        var rate = new Rate(3, 4);

        Assert.Equal(0.75, rate.Value);
        Assert.True(rate >= 0.5);       // implicit conversion — the Gate compares rates like numbers
        Assert.Equal(0.75, rate);
    }

    [Fact]
    public void Rate_FormatsAsAPercentageWithItsInterval()
        => Assert.Equal("100 [76–100]", new Rate(12, 12).ToString());

    [Fact]
    public void Rate_WithNoItems_ReadsAsNotApplicable()
    {
        Assert.Equal("n/a", new Rate(0, 0).ToString());
        Assert.Equal(0.0, new Rate(0, 0).Value);
    }

    /// The counts travel with the rate — this is what stops 12/12 and 1200/1200 reading alike.
    [Fact]
    public void Rate_CarriesItsCounts()
    {
        var small = new Rate(12, 12);
        var large = new Rate(1200, 1200);

        Assert.Equal(small.Value, large.Value);
        Assert.True(large.Lower > small.Lower);
    }

    [Fact]
    public void Scorecard_ExposesEveryRateWithAnInterval()
    {
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var card = RepositoryBenchmark.Scorecards()[labelOracle.Name];

        var rates = new[]
        {
            card.Coverage,
            card.SelectiveAccuracy,
            card.SelectiveRisk,
            card.DecisionAccuracy,
            card.AbstentionRecall,
            card.UnsupportedAnswerRate,
            card.OverabstentionRate,
            card.CertaintyAccuracy,
            card.UrgencyAccuracy,
            card.UndertriageRate,
            card.ContrastAccuracy,
            card.PairedRevisionAccuracy,
            card.OriginalTargetPersistence,
            card.ContrastCertaintyAccuracy,
            card.ContrastUrgencyAccuracy,
            card.ContrastUndertriageRate
        };

        foreach (var rate in rates)
        {
            var expectedInterval = Wilson.Interval(rate.Successes, rate.Total);
            Assert.Equal(expectedInterval.Lower, rate.Lower);
            Assert.Equal(expectedInterval.Upper, rate.Upper);
            Assert.InRange(rate.Value, rate.Lower, rate.Upper);
        }
    }
}
