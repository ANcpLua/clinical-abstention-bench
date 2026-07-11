using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class PromptTests
{
    private const string LiveModelTag = "llama3.2:3b";
    private static PromptFile Prompts => Bench.LoadPrompts(RepositoryBenchmark.DataDirectory);

    [Fact]
    public void LoadPrompts_CarriesTheFourArmsOfThePromptSweep()
    {
        var file = Prompts;

        Assert.Equal("abstention-offered", file.Default);
        Assert.Equal(
            ["abstention-offered", "abstention-unmentioned", "abstention-encouraged", "no-forced-choice"],
            file.Prompts.Select(p => p.Name));

        foreach (var p in file.Prompts)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Text), $"{p.Name} has no text");
            Assert.False(string.IsNullOrWhiteSpace(p.Description), $"{p.Name} does not say what it is for");
        }
    }

    /// The four prompts differ in the dimensions the sweep is meant to isolate: whether and
    /// how hard, the model is pushed toward declining.
    [Fact]
    public void ThePromptsDifferInHowHardTheyPushTowardAbstention()
    {
        var byName = Prompts.Prompts.ToDictionary(p => p.Name);

        // Offered: the option is stated, once, flatly.
        Assert.Contains("INSUFFICIENT INFORMATION", byName["abstention-offered"].Text);

        // Unmentioned: the model is never told it may decline. A model that declines here does so
        // unprompted — the only condition under which abstention is a disposition and not compliance.
        Assert.DoesNotContain("INSUFFICIENT", byName["abstention-unmentioned"].Text);
        Assert.DoesNotContain("sufficient", byName["abstention-unmentioned"].Text);

        // Encouraged: the asymmetry of the error is named out loud.
        Assert.Contains("worse than declining", byName["abstention-encouraged"].Text);

        // No-forced-choice: the words that create the contradiction are gone.
        Assert.Contains("single most likely", byName["abstention-offered"].Text);
        Assert.DoesNotContain("single most likely", byName["no-forced-choice"].Text);
    }

    [Fact]
    public void SelectPrompts_WithNoSelection_UsesTheDefault()
    {
        var selected = Bench.SelectPrompts(Prompts, []);

        Assert.Equal(Prompts.Default, selected.Single().Name);
    }

    [Fact]
    public void SelectPrompts_All_SweepsEveryPrompt()
        => Assert.Equal(Prompts.Prompts.Count, Bench.SelectPrompts(Prompts, ["all"]).Count);

    [Fact]
    public void SelectPrompts_MatchesCaseInsensitivelyAndDeduplicates()
    {
        var file = Prompts;
        var target = file.Prompts.First(p => p.Name != file.Default);
        var selected = Bench.SelectPrompts(file, [target.Name.ToUpperInvariant(), target.Name]);

        Assert.Same(target, selected.Single());
    }

    /// Fail-closed: a typo must not silently fall back to the default and quietly change what the run
    /// measured.
    [Fact]
    public void SelectPrompts_UnknownName_Throws()
    {
        var unknown = Prompts.Default + "-typo";
        var ex = Assert.Throws<InvalidOperationException>(() => Bench.SelectPrompts(Prompts, [unknown]));

        Assert.Contains("matches no prompt", ex.Message);
        Assert.Contains(Prompts.Default, ex.Message);
    }

    /// The prompt is part of a live model's identity, because the number is a claim about the pair.
    [Fact]
    public void ALiveModelsNameCarriesItsPrompt()
    {
        var prompts = Bench.SelectPrompts(Prompts, ["all"]);
        var models = prompts.Select(p => new OllamaModel(LiveModelTag, p)).ToList();

        Assert.Equal(prompts.Select(p => $"{LiveModelTag} @ {p.Name}"), models.Select(m => m.Name));

        Assert.All(models, m => Assert.False(m.IsBaseline));
    }

    /// A programmatic reference policy never sees a system prompt. That is exactly why it is an
    /// analytical reference point rather than a competitor, and the report says so.
    [Fact]
    public void AReferencePolicyNeverSeesASystemPrompt_WhicheverPromptWasSelected()
    {
        foreach (var model in RepositoryBenchmark.ReferenceModels)
        {
            var reference = Assert.IsType<ReferencePolicyModel>(model);

            Assert.True(model.IsBaseline);
            Assert.Null(model.SystemPrompt);
            Assert.Equal(reference.Policy.ToString(), model.Provenance["policy"]);
        }
    }

    /// Sweeping a live model must produce one scorecard per prompt, not one overwritten by the last.
    [Fact]
    public void SelectModels_ByBareModelTag_SelectsEveryPromptVariantOfIt()
    {
        var prompts = Bench.SelectPrompts(Prompts, ["all"]);
        IReadOnlyList<IModel> available =
        [
            .. RepositoryBenchmark.ReferenceModels,
            .. prompts.Select(p => new OllamaModel(LiveModelTag, p))
        ];

        var swept = Bench.SelectModels(available, [LiveModelTag]);
        Assert.Equal(prompts.Count, swept.Count);
        Assert.All(swept, m => Assert.StartsWith(LiveModelTag + " @ ", m.Name));

        // ...and an exact name still selects exactly one.
        var exactName = $"{LiveModelTag} @ {prompts[0].Name}";
        var one = Bench.SelectModels(available, [exactName]);
        Assert.Equal(exactName, one.Single().Name);
    }
}
