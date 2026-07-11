using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class GateTests
{
    private static Scorecard CardWithRecall(string name, int correctAbstentions, int ablatedTotal)
        => new(name,
            AblatedTotal: ablatedTotal,
            CorrectAbstentions: correctAbstentions,
            UnsupportedAnswers: ablatedTotal - correctAbstentions,
            FullTotal: 0, CorrectAnswers: 0, WrongAnswers: 0, OverAbstentions: 0);

    [Fact]
    public void Check_PassesWhenEveryModelMeetsOrExceedsTheThreshold()
    {
        const int total = 10;
        const double threshold = 0.9;
        var atThreshold = (int)(total * threshold);
        var gate = Gate.Check(
            [CardWithRecall("AboveThreshold", total, total), CardWithRecall("AtThreshold", atThreshold, total)],
            threshold);

        Assert.True(gate.Passed);
        Assert.Empty(gate.Failures);
    }

    [Fact]
    public void Check_FailsAndNamesTheModelAndTheMetricBelowTheThreshold()
    {
        const string failingModel = "BelowThreshold";
        const int total = 10;
        const double threshold = 0.9;
        var belowThreshold = (int)(total * threshold) - 1;
        var gate = Gate.Check(
            [CardWithRecall("AboveThreshold", total, total), CardWithRecall(failingModel, belowThreshold, total)],
            threshold);

        Assert.False(gate.Passed);
        Assert.Equal([failingModel], gate.FailingModels);

        var failure = gate.Failures.Single();
        Assert.Equal("abstention-recall", failure.Metric);
        Assert.Equal((double)belowThreshold / total, failure.Actual);
        Assert.Equal(threshold, failure.Threshold);
        // Pinned to the invariant culture — a CI runner in a different locale must log the same string.
        Assert.Equal("BelowThreshold (abstention-recall 80 % < 90 %)", failure.ToString());
    }

    /// With no threshold set there is nothing to enforce, and the run must not fail.
    [Fact]
    public void Check_WithNoThresholds_Passes()
    {
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var card = RepositoryBenchmark.Scorecards()[alwaysAnswer.Name];

        Assert.True(Gate.Check([card]).Passed);
    }

    /// The defect this flag had: an unfiltered demo run always contains AlwaysAnswerBaseline,
    /// whose recall is 0 by construction, so every gate failed regardless of the real model.
    /// Selecting the model under test is what makes the gate usable.
    [Fact]
    public async Task Check_OnRepositoryData_FailsUnfiltered_ButPassesForTheLabelOracle()
    {
        var available = RepositoryBenchmark.ReferenceModels;

        async Task<Scorecard> Card(IModel model)
        {
            var results = await Bench.RunModelAsync(model, RepositoryBenchmark.Items);
            return Scorecard.From(model.Name, results);
        }

        var everything = await Task.WhenAll(Bench.SelectModels(available, []).Select(Card));
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        Assert.False(Gate.Check(everything, 0.9).Passed);
        Assert.Contains(alwaysAnswer.Name, Gate.Check(everything, 0.9).FailingModels);

        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var labelOracleOnly = await Task.WhenAll(Bench.SelectModels(available, [labelOracle.Name]).Select(Card));
        Assert.True(Gate.Check(labelOracleOnly, 0.9).Passed);
    }
}

public class ModelSelectionTests
{
    private static readonly IReadOnlyList<IModel> Available = RepositoryBenchmark.ReferenceModels;

    [Fact]
    public void SelectModels_EmptySelection_RunsEverything()
        => Assert.Equal(Available.Select(model => model.Name), Bench.SelectModels(Available, []).Select(model => model.Name));

    [Fact]
    public void SelectModels_MatchesCaseInsensitivelyAndDeduplicates()
    {
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var selected = Bench.SelectModels(
            Available,
            [labelOracle.Name.ToLowerInvariant(), labelOracle.Name.ToUpperInvariant()]);

        Assert.Same(labelOracle, Assert.Single(selected));
    }

    /// Fail-closed: a typo in --only must not silently gate nothing and exit 0.
    [Fact]
    public void SelectModels_UnknownName_Throws()
    {
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var unknownName = labelOracle.Name + "-missing";
        var ex = Assert.Throws<InvalidOperationException>(() => Bench.SelectModels(Available, [unknownName]));

        Assert.Contains("matched no model", ex.Message);
        Assert.Contains(unknownName, ex.Message);
        foreach (var model in Available)
            Assert.Contains(model.Name, ex.Message);
    }
}
