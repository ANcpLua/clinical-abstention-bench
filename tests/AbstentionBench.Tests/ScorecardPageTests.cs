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
}
