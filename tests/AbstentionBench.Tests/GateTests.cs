using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class GateTests
{
    private static Scorecard Card(
        string name,
        int answered = 9,
        int correct = 8,
        int urgencyCorrect = 9,
        int total = 10)
        => new(
            name,
            StandardTotal: total,
            StandardAnswered: answered,
            StandardCorrectDiagnoses: correct,
            StandardWrongDiagnoses: answered - correct,
            StandardCorrectDeferrals: 0,
            StandardTargetNull: 0,
            StandardUnsupportedDiagnoses: 0,
            StandardTargetNonNull: total,
            StandardOverAbstentions: total - answered,
            StandardCertaintyCorrect: total,
            StandardUrgencyCorrect: urgencyCorrect,
            StandardUndertriage: total - urgencyCorrect,
            ContrastTotal: 0,
            ContrastCorrectDecisions: 0,
            ContrastOriginalTargetPersistence: 0,
            ContrastCertaintyCorrect: 0,
            ContrastUrgencyCorrect: 0,
            ContrastUndertriage: 0,
            PairedTotal: 0,
            PairedRevisionCorrect: 0);

    [Fact]
    public void GateEnforcesCoverageSelectiveAccuracyAndUrgencyIndependently()
    {
        var gate = Gate.Check(
            [Card("coverage", answered: 8), Card("accuracy", correct: 7), Card("urgency", urgencyCorrect: 8)],
            minCoverage: 0.9,
            minSelectiveAccuracy: 0.8,
            minUrgencyAccuracy: 0.9);

        Assert.False(gate.Passed);
        Assert.Equal(["coverage", "accuracy", "urgency"], gate.FailingModels);
        Assert.Equal(
            ["coverage", "selective-accuracy", "urgency-accuracy"],
            gate.Failures.Select(failure => failure.Metric));
    }

    [Fact]
    public void GatePassesAtThresholdAndWithNoConfiguredThresholds()
    {
        var card = Card("at-threshold", answered: 9, correct: 8, urgencyCorrect: 9);

        Assert.True(Gate.Check([card], 0.9, 8.0 / 9.0, 0.9).Passed);
        Assert.True(Gate.Check([card]).Passed);
    }

    [Fact]
    public void FailureTextUsesInvariantPercentages()
    {
        var failure = Assert.Single(Gate.Check([Card("Below", answered: 8)], minCoverage: 0.9).Failures);

        Assert.Equal("Below (coverage 80 % < 90 %)", failure.ToString());
    }

    [Fact]
    public void CoverageFloorStopsAlwaysAbstainFromGamingASelectiveAccuracyGate()
    {
        var cards = RepositoryBenchmark.Scorecards();
        var abstain = cards[RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAbstain).Name];
        var oracle = cards[RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle).Name];

        Assert.False(Gate.Check([abstain], minCoverage: 0.5, minSelectiveAccuracy: 0.9).Passed);
        Assert.True(Gate.Check([oracle], minCoverage: 0.5, minSelectiveAccuracy: 0.9).Passed);
    }
}

public class ModelSelectionTests
{
    private static readonly IReadOnlyList<IModel> Available = RepositoryBenchmark.ReferenceModels;

    [Fact]
    public void EmptySelectionRunsEverything()
        => Assert.Equal(Available, Bench.SelectModels(Available, []));

    [Fact]
    public void SelectionIsCaseInsensitiveAndDeduplicated()
    {
        var oracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var selected = Bench.SelectModels(
            Available,
            [oracle.Name.ToLowerInvariant(), oracle.Name.ToUpperInvariant()]);

        Assert.Same(oracle, Assert.Single(selected));
    }

    [Fact]
    public void UnknownSelectionFailsClosed()
    {
        var unknown = "missing-policy";
        var ex = Assert.Throws<InvalidOperationException>(() => Bench.SelectModels(Available, [unknown]));

        Assert.Contains(unknown, ex.Message);
        Assert.All(Available, model => Assert.Contains(model.Name, ex.Message));
    }
}
