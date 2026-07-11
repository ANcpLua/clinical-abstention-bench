using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScorecardPageTests
{
    [Fact]
    public void Render_ContainsEveryModelAndMetric_AndExampleCase()
    {
        var dataDir = Bench.FindDataDir();
        var cases = Bench.LoadCases(dataDir);
        var items = Bench.ItemsFor(cases);
        var models = Bench.LoadDemoModels(dataDir);
        var cards = models
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToList();

        var html = ScorecardPage.Render(cases.Count, items.Count, cards, cases[0]);

        Assert.StartsWith("<!doctype html>", html);
        foreach (var card in cards)
            Assert.Contains(card.ModelName, html);
        foreach (var metric in new[] { "abstain-recall", "unsupported", "answer-acc", "over-abstain", "selective-acc" })
            Assert.Contains(metric, html);
        Assert.Contains(cases[0].Condition, html);
        Assert.Contains("INSUFFICIENT", html);
        // Self-contained: no external fetches.
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", ""));
        Assert.DoesNotContain("<script", html);
    }

    /// No rate is ever shown on this page without its interval — at n = 12 the point estimate on its
    /// own is the most misleading thing the page could print.
    [Fact]
    public void Render_ShowsEveryRateWithItsWilsonInterval()
    {
        var dataDir = Bench.FindDataDir();
        var cases = Bench.LoadCases(dataDir);
        var items = Bench.ItemsFor(cases);
        var cards = Bench.LoadDemoModels(dataDir)
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToList();

        var html = ScorecardPage.Render(cases.Count, items.Count, cards, cases[0]);

        // CalibratedBaseline is 100 % on abstention recall over 12 items -> [76 %, 100 %].
        Assert.Contains("[76 %–100 %]", html);
        // AlwaysAnswerBaseline is 0 % over the same 12 -> [0 %, 24 %].
        Assert.Contains("[0 %–24 %]", html);
        Assert.Contains("Wilson score interval", html);

        // One interval per rate per model — five on the scorecard, three on the counterfactual probe.
        Assert.Equal(cards.Count * 8, html.Split("""<span class="ci">""").Length - 1);
    }

    [Fact]
    public void Render_ShowsTheCounterfactualProbeAndTheThreeVariantsOfACase()
    {
        var dataDir = Bench.FindDataDir();
        var cases = Bench.LoadCases(dataDir);
        var items = Bench.ItemsFor(cases);
        var cards = Bench.LoadDemoModels(dataDir)
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToList();

        var html = ScorecardPage.Render(cases.Count, items.Count, cards, cases[0]);

        Assert.Contains("Counterfactual probe", html);
        Assert.Contains("evidence-sensitivity", html);
        Assert.Contains("said the excluded diagnosis", html);

        // All three variants of the example case are shown side by side.
        Assert.Contains(cases[0].FullPrompt, html);
        Assert.Contains(cases[0].AblatedPrompt, html);
        Assert.Contains(cases[0].CounterfactualPrompt, html);

        // And the page says out loud why the probe is not part of the headline score.
        Assert.Contains("trivially maximised by a model that answers nothing", html);
    }
}
