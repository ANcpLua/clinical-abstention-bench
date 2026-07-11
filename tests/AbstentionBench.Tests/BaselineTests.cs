using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

/// The three programmatic reference policies are the two degenerate poles and the scoring target.
/// Together they demonstrate that a model can fail by never declining, it can fail by always
/// declining, and only the label oracle scores well by construction.
public class BaselineTests
{
    private static Dictionary<string, Scorecard> RunAll() => RepositoryBenchmark.Scorecards();

    private static Scorecard Card(IReadOnlyDictionary<string, Scorecard> cards, ReferencePolicy policy)
        => cards[RepositoryBenchmark.Policy(policy).Name];

    /// Abstention-recall — the headline — is trivially maximised by refusing to answer anything.
    /// Nothing in the repo demonstrated that a model doing so is still scored as a failure, so a
    /// reader had to take it on faith. This is the demonstration.
    [Fact]
    public void AlwaysAbstain_MaxesTheHeadlineMetric_AndIsStillScoredAsAFailure()
    {
        var cards = RunAll();
        var abstain = Card(cards, ReferencePolicy.AlwaysAbstain);
        var answer = Card(cards, ReferencePolicy.AlwaysAnswer);

        // A perfect score on the metric the benchmark is named after...
        Assert.Equal(1.0, abstain.AbstentionRecall.Value);
        Assert.Equal(0.0, abstain.UnsupportedAnswerRate.Value);

        // ...bought by answering nothing at all.
        Assert.Equal(0.0, abstain.AnswerAccuracy.Value);
        Assert.Equal(1.0, abstain.OverAbstentionRate.Value);

        // Selective accuracy is what refuses to be gamed: the model that never declines and the model
        // that always declines land on exactly the same score. Each is right on half the benchmark.
        Assert.Equal(0.5, abstain.SelectiveAccuracy.Value);
        Assert.Equal(answer.SelectiveAccuracy.Value, abstain.SelectiveAccuracy.Value);

        // And the label oracle, which emits the configured target by construction, beats them both.
        Assert.True(Card(cards, ReferencePolicy.LabelOracle).SelectiveAccuracy.Value > abstain.SelectiveAccuracy.Value);
    }

    /// The hole this baseline exposes in the CI gate: `--gate` alone checks abstention-recall, and a
    /// model that says nothing scores 100 % on it. The answer-accuracy floor is what closes it.
    [Fact]
    public void AlwaysAbstain_PassesARecallOnlyGate_ButFailsWhenAnAccuracyFloorIsAdded()
    {
        var cards = RunAll();
        var abstain = Card(cards, ReferencePolicy.AlwaysAbstain);

        Assert.True(Gate.Check([abstain], minAbstentionRecall: 0.9).Passed);

        var both = Gate.Check([abstain], minAbstentionRecall: 0.9, minAnswerAccuracy: 0.9);
        Assert.False(both.Passed);
        Assert.Equal("answer-accuracy", both.Failures.Single().Metric);
    }

    /// ...and the mirror image: the model that always answers passes an accuracy floor and fails on
    /// recall. Only a model that does both clears a gate with both thresholds set.
    [Fact]
    public void OnlyTheLabelOracleClearsAGateOnBothMetrics()
    {
        var cards = RunAll();

        GateResult Both(ReferencePolicy policy)
            => Gate.Check([Card(cards, policy)], minAbstentionRecall: 0.9, minAnswerAccuracy: 0.9);

        Assert.False(Both(ReferencePolicy.AlwaysAnswer).Passed);
        Assert.False(Both(ReferencePolicy.AlwaysAbstain).Passed);
        Assert.True(Both(ReferencePolicy.LabelOracle).Passed);

        Assert.Equal("abstention-recall", Both(ReferencePolicy.AlwaysAnswer).Failures.Single().Metric);
        Assert.Equal("answer-accuracy", Both(ReferencePolicy.AlwaysAbstain).Failures.Single().Metric);
    }

    [Fact]
    public async Task EveryReferencePolicyReturnsAResponseForEveryRepositoryItem()
    {
        foreach (var model in RepositoryBenchmark.ReferenceModels)
        {
            var results = await Bench.RunModelAsync(model, RepositoryBenchmark.Items);

            Assert.Equal(RepositoryBenchmark.Items.Count, results.Count);
            Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Response)));
        }
    }
}
