using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScoringTests
{
    [Fact]
    public void FromCase_ProducesOneAnswerableAndOneMustAbstain()
    {
        var c = new BenchCase("c99", "Test", "full?", "ablated?", "Answer", "the fact", "why", ["Ans"]);
        var items = Item.FromCase(c).ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i is { Variant: Variant.Full, MustAbstain: false });
        Assert.Contains(items, i => i is { Variant: Variant.Ablated, MustAbstain: true, GroundTruth: "INSUFFICIENT" });
        Assert.Equal("c99:ablated", items.Single(i => i.MustAbstain).Key);
    }

    /// The answerable item carries the case's synonyms; the must-abstain item has nothing to accept.
    [Fact]
    public void FromCase_AcceptedFormsCoverTheCanonicalAnswerAndItsSynonyms()
    {
        var c = new BenchCase("c99", "Test", "full?", "ablated?", "Gout", "the fact", "why", ["podagra"]);
        var items = Item.FromCase(c).ToList();

        var full = items.Single(i => i.Variant == Variant.Full);
        Assert.Equal(["Gout", "podagra"], full.AcceptedForms);

        var ablated = items.Single(i => i.Variant == Variant.Ablated);
        Assert.Equal(["INSUFFICIENT"], ablated.AcceptedForms);
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
        Assert.Equal(cases.Count * 2, items.Count);

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
