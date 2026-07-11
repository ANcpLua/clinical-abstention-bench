using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScoringTests
{
    private static BenchCase Case(string answer = "Answer", params string[] synonyms)
        => new("c99", "Test", "full?", "ablated?", "counterfactual?", answer, "the fact", "why", synonyms);

    [Fact]
    public void FromCase_ProducesOneAnswerableAndTwoMustAbstain()
    {
        var items = Item.FromCase(Case()).ToList();

        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i is { Variant: Variant.Full, MustAbstain: false });
        Assert.Contains(items, i => i is { Variant: Variant.Ablated, MustAbstain: true, GroundTruth: "INSUFFICIENT" });
        Assert.Contains(items, i => i is { Variant: Variant.Counterfactual, MustAbstain: true, GroundTruth: "INSUFFICIENT" });
        Assert.Equal(["c99:full", "c99:ablated", "c99:counterfactual"], items.Select(i => i.Key));
    }

    /// The answerable item carries the case's synonyms; the must-abstain items have nothing to accept.
    /// The counterfactual item carries the same list as what it must NOT say.
    [Fact]
    public void FromCase_AcceptedFormsCoverTheCanonicalAnswerAndItsSynonyms()
    {
        var items = Item.FromCase(Case("Gout", "podagra")).ToList();

        var full = items.Single(i => i.Variant == Variant.Full);
        Assert.Equal(["Gout", "podagra"], full.AcceptedForms);
        Assert.Empty(full.ExcludedForms);

        var ablated = items.Single(i => i.Variant == Variant.Ablated);
        Assert.Equal(["INSUFFICIENT"], ablated.AcceptedForms);
        Assert.Empty(ablated.ExcludedForms);

        // The counterfactual item's job is to know what the flipped evidence now rules OUT.
        var counterfactual = items.Single(i => i.Variant == Variant.Counterfactual);
        Assert.Equal(["Gout", "podagra"], counterfactual.ExcludedForms);
    }

    [Fact]
    public void Scorecard_AggregatesRatesCorrectly()
    {
        var full = new Item("c1", Variant.Full, "", "Flu", false);
        var ablated = new Item("c1", Variant.Ablated, "", "INSUFFICIENT", true);
        ItemResult R(Item i, string resp) => new("m", i, null, resp, LexicalGrader.Instance.Score(i, resp));

        var card = Scorecard.From("m",
        [
            R(full, "Flu"),                // CorrectAnswer
            R(full, "Cold"),               // WrongAnswer
            R(ablated, "INSUFFICIENT"),    // CorrectAbstention
            R(ablated, "Flu")              // UnsupportedAnswer
        ]);

        Assert.Equal(2, card.FullTotal);
        Assert.Equal(2, card.AblatedTotal);
        Assert.Equal(0.5, card.AnswerAccuracy);
        Assert.Equal(0.5, card.AbstentionRecall);
        Assert.Equal(0.5, card.UnsupportedAnswerRate);
        Assert.Equal(0.5, card.SelectiveAccuracy); // 2 of 4 items matched what the evidence supported
    }
}

public class DatasetIntegrationTests
{
    [Fact]
    public void Baselines_BehaveAsAdvertised_OnRealData()
    {
        var dataDir = Bench.FindDataDir();
        var cases = Bench.LoadCases(dataDir);
        var items = Bench.ItemsFor(cases);
        var models = Bench.LoadDemoModels(dataDir);

        Assert.True(cases.Count >= 10, "expected a non-trivial dataset");
        Assert.Equal(cases.Count * 3, items.Count); // full + ablated + counterfactual

        var cards = models
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToDictionary(c => c.ModelName);

        // The always-answer baseline never abstains -> every ablated item is an unsupported answer.
        Assert.Equal(1.0, cards["AlwaysAnswerBaseline"].UnsupportedAnswerRate);
        Assert.Equal(0.0, cards["AlwaysAnswerBaseline"].AbstentionRecall);

        // The calibrated baseline abstains on every ablated item and answers the rest correctly.
        Assert.Equal(1.0, cards["CalibratedBaseline"].AbstentionRecall);
        Assert.Equal(1.0, cards["CalibratedBaseline"].AnswerAccuracy);

        // The whole point: the benchmark separates them.
        Assert.True(cards["CalibratedBaseline"].SelectiveAccuracy > cards["AlwaysAnswerBaseline"].SelectiveAccuracy);
    }
}
