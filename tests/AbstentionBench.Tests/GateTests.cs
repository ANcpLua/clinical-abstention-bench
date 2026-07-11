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
    public void Check_PassesWhenEveryModelClearsTheThreshold()
    {
        var gate = Gate.Check([CardWithRecall("Above", 10, 10), CardWithRecall("AlsoAbove", 9, 10)], 0.9);

        Assert.True(gate.Passed);
        Assert.Empty(gate.FailingModels);
    }

    [Fact]
    public void Check_FailsAndNamesTheModelBelowTheThreshold()
    {
        var gate = Gate.Check([CardWithRecall("Above", 10, 10), CardWithRecall("Below", 0, 10)], 0.9);

        Assert.False(gate.Passed);
        Assert.Equal(["Below"], gate.FailingModels);
    }

    /// The defect this flag had: an unfiltered demo run always contains AlwaysAnswerBaseline,
    /// whose recall is 0 by construction, so every gate failed regardless of the real model.
    /// Selecting the model under test is what makes the gate usable.
    [Fact]
    public void Check_OnRealData_FailsUnfiltered_ButPassesForTheSelectedModel()
    {
        var dataDir = Bench.FindDataDir();
        var items = Bench.ItemsFor(Bench.LoadCases(dataDir));
        var available = Bench.LoadDemoModels(dataDir);

        Scorecard Card(IModel m) => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult());

        var everything = Bench.SelectModels(available, []).Select(Card).ToList();
        Assert.False(Gate.Check(everything, 0.9).Passed);
        Assert.Contains("AlwaysAnswerBaseline", Gate.Check(everything, 0.9).FailingModels);

        var calibratedOnly = Bench.SelectModels(available, ["CalibratedBaseline"]).Select(Card).ToList();
        Assert.True(Gate.Check(calibratedOnly, 0.9).Passed);
    }
}

public class ModelSelectionTests
{
    private static readonly IReadOnlyList<IModel> Available =
    [
        new ScriptedModel("AlwaysAnswerBaseline", new Dictionary<string, string>()),
        new ScriptedModel("CalibratedBaseline", new Dictionary<string, string>())
    ];

    [Fact]
    public void SelectModels_EmptySelection_RunsEverything()
        => Assert.Equal(2, Bench.SelectModels(Available, []).Count);

    [Fact]
    public void SelectModels_MatchesCaseInsensitivelyAndDeduplicates()
    {
        var selected = Bench.SelectModels(Available, ["calibratedbaseline", "CalibratedBaseline"]);

        Assert.Single(selected);
        Assert.Equal("CalibratedBaseline", selected[0].Name);
    }

    /// Fail-closed: a typo in --only must not silently gate nothing and exit 0.
    [Fact]
    public void SelectModels_UnknownName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bench.SelectModels(Available, ["CalibratedBaselin"]));

        Assert.Contains("matched no model", ex.Message);
        Assert.Contains("CalibratedBaseline", ex.Message);
    }
}
