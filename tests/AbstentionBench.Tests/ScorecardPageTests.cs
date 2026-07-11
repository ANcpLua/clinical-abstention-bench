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

        // One interval per rate per model — five rates, two baselines.
        Assert.Equal(cards.Count * 5, html.Split("""<span class="ci">""").Length - 1);
    }
}
