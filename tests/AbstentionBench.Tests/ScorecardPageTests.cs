using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScorecardPageTests
{
    private static string Render()
    {
        var cards = RepositoryBenchmark.Scorecards().Values.ToList();
        var baselineNames = RepositoryBenchmark.ReferenceModels
            .Select(model => model.Name)
            .ToHashSet(StringComparer.Ordinal);
        return ScorecardPage.Render(
            RepositoryBenchmark.Cases.Count,
            RepositoryBenchmark.Items.Count,
            cards,
            RepositoryBenchmark.Cases[0],
            baselineNames);
    }

    [Fact]
    public void HtmlShowsTheV2PrimaryAndContrastMetrics()
    {
        var html = Render();

        Assert.StartsWith("<!doctype html>", html);
        foreach (var metric in new[]
                 {
                     "coverage", "selective acc", "decision acc", "certainty acc", "urgency acc",
                     "undertriage", "contrast acc", "original persists", "paired revision"
                 })
            Assert.Contains(metric, html, StringComparison.OrdinalIgnoreCase);
        foreach (var model in RepositoryBenchmark.ReferenceModels)
            Assert.Contains(model.Name, html);
        Assert.Contains("programmatic reference · no system prompt", html);
        Assert.Contains("95% Wilson intervals", html);
    }

    [Fact]
    public void HtmlShowsAllThreeEvidenceStatesAndTheirStructuredTargets()
    {
        var html = Render();
        var example = RepositoryBenchmark.Cases[0];

        Assert.Contains(example.Full.Vignette, html);
        Assert.Contains(example.Ablated.Vignette, html);
        Assert.Contains(example.Contrast.Vignette, html);
        Assert.Contains(example.Full.Target.Diagnosis!, html);
        Assert.Contains(example.Contrast.Target.Diagnosis!, html);
        Assert.Contains("diagnostic deferral", html);
        Assert.Contains("alternative-supported", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlIsSelfContainedAndContainsNoLegacyEvidenceBlindnessClaims()
    {
        var html = Render();

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot have read", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("counterfactual", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSUFFICIENT", html, StringComparison.OrdinalIgnoreCase);
    }
}
