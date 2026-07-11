using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

/// The three fixtures are the two degenerate poles and the target. Together they are the argument
/// that the benchmark measures something: a model can fail by never declining, it can fail by always
/// declining, and only one behaviour scores well.
public class BaselineTests
{
    private static Dictionary<string, Scorecard> RunAll()
    {
        var dataDir = Bench.FindDataDir();
        var items = Bench.ItemsFor(Bench.LoadCases(dataDir));

        return Bench.LoadDemoModels(dataDir)
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToDictionary(c => c.ModelName);
    }

    /// Abstention-recall — the headline — is trivially maximised by refusing to answer anything.
    /// Nothing in the repo demonstrated that a model doing so is still scored as a failure, so a
    /// reader had to take it on faith. This is the demonstration.
    [Fact]
    public void AlwaysAbstain_MaxesTheHeadlineMetric_AndIsStillScoredAsAFailure()
    {
        var cards = RunAll();
        var abstain = cards["AlwaysAbstainBaseline"];
        var answer = cards["AlwaysAnswerBaseline"];

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

        // And the one that reads the evidence beats them both.
        Assert.True(cards["CalibratedBaseline"].SelectiveAccuracy.Value > abstain.SelectiveAccuracy.Value);
    }

    /// The hole this baseline exposes in the CI gate: `--gate` alone checks abstention-recall, and a
    /// model that says nothing scores 100 % on it. The answer-accuracy floor is what closes it.
    [Fact]
    public void AlwaysAbstain_PassesARecallOnlyGate_ButFailsWhenAnAccuracyFloorIsAdded()
    {
        var abstain = RunAll()["AlwaysAbstainBaseline"];

        Assert.True(Gate.Check([abstain], minAbstentionRecall: 0.9).Passed);

        var both = Gate.Check([abstain], minAbstentionRecall: 0.9, minAnswerAccuracy: 0.9);
        Assert.False(both.Passed);
        Assert.Equal("answer-accuracy", both.Failures.Single().Metric);
    }

    /// ...and the mirror image: the model that always answers passes an accuracy floor and fails on
    /// recall. Only a model that does both clears a gate with both thresholds set.
    [Fact]
    public void OnlyTheCalibratedBaselineClearsAGateOnBothMetrics()
    {
        var cards = RunAll();

        GateResult Both(string model) => Gate.Check([cards[model]], minAbstentionRecall: 0.9, minAnswerAccuracy: 0.9);

        Assert.False(Both("AlwaysAnswerBaseline").Passed);
        Assert.False(Both("AlwaysAbstainBaseline").Passed);
        Assert.True(Both("CalibratedBaseline").Passed);

        Assert.Equal("abstention-recall", Both("AlwaysAnswerBaseline").Failures.Single().Metric);
        Assert.Equal("answer-accuracy", Both("AlwaysAbstainBaseline").Failures.Single().Metric);
    }

    [Fact]
    public async Task EveryBaselineAnswersEveryItem()
    {
        var dataDir = Bench.FindDataDir();
        var items = Bench.ItemsFor(Bench.LoadCases(dataDir));

        foreach (var model in Bench.LoadDemoModels(dataDir))
        {
            var results = await Bench.RunModelAsync(model, items);

            Assert.Equal(items.Count, results.Count);
            Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Response)));
        }
    }
}
