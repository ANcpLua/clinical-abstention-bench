using System.Globalization;
using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScorecardPageTests
{
    private static readonly IReadOnlyList<Scorecard> Cards = RepositoryBenchmark.Scorecards().Values.ToList();

    private static readonly IReadOnlySet<string> BaselineNames = RepositoryBenchmark.ReferenceModels
        .Where(model => model.IsBaseline)
        .Select(model => model.Name)
        .ToHashSet(StringComparer.Ordinal);

    private static string Render()
        => ScorecardPage.Render(
            RepositoryBenchmark.Cases.Count,
            RepositoryBenchmark.Items.Count,
            Cards,
            RepositoryBenchmark.Cases[0],
            BaselineNames);

    private static string RenderedInterval(Rate rate)
        => $"[{Percent(rate.Lower)}–{Percent(rate.Upper)}]";

    private static string Percent(double value)
        => Math.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    [Fact]
    public void Render_ContainsEveryModelAndMetric_AndExampleCase()
    {
        var html = Render();

        Assert.StartsWith("<!doctype html>", html);
        foreach (var card in Cards)
            Assert.Contains(card.ModelName, html);
        foreach (var metric in new[] { "abstain-recall", "unsupported", "answer-acc", "over-abstain", "selective-acc" })
            Assert.Contains(metric, html);
        Assert.Contains(RepositoryBenchmark.Cases[0].Condition, html);
        Assert.Contains("INSUFFICIENT", html);
        Assert.Contains("programmatic reference · no system prompt", html);
        Assert.DoesNotContain("<tr class=\"live\">", html);

        // Self-contained: no external fetches.
        Assert.False(html.Contains("http://", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains("<script", StringComparison.OrdinalIgnoreCase));
    }

    /// No rate is ever shown on this page without its interval — the point estimate on its own is the
    /// most misleading thing the page could print.
    [Fact]
    public void Render_ShowsEveryRateWithItsWilsonInterval()
    {
        var html = Render();

        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        Assert.Contains(RenderedInterval(Cards.Single(c => c.ModelName == labelOracle.Name).AbstentionRecall), html);
        Assert.Contains(RenderedInterval(Cards.Single(c => c.ModelName == alwaysAnswer.Name).AbstentionRecall), html);
        Assert.Contains("Wilson score interval", html);

        // One interval per rate per model — five on the scorecard, three on the counterfactual probe.
        Assert.Equal(Cards.Count * 8, html.Split("""<span class="ci">""").Length - 1);
    }

    [Fact]
    public void Render_ShowsTheCounterfactualProbeAndTheThreeVariantsOfACase()
    {
        var html = Render();
        var example = RepositoryBenchmark.Cases[0];

        Assert.Contains("Counterfactual probe", html);
        Assert.Contains("evidence-sensitivity", html);
        Assert.Contains("said the excluded diagnosis", html);

        // All three variants of the example case are shown side by side.
        Assert.Contains(example.FullPrompt, html);
        Assert.Contains(example.AblatedPrompt, html);
        Assert.Contains(example.CounterfactualPrompt, html);

        // And the page says out loud why the probe is not part of the headline score.
        Assert.Contains("trivially maximised by a model that answers nothing", html);
    }
}
