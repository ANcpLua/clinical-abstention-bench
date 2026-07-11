using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class PromptTests
{
    private static PromptFile Prompts => Bench.LoadPrompts(Bench.FindDataDir());

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

    /// The three prompts differ in exactly the dimension the sweep is meant to isolate: whether, and
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

        Assert.Equal("abstention-offered", selected.Single().Name);
    }

    [Fact]
    public void SelectPrompts_All_SweepsEveryPrompt()
        => Assert.Equal(Prompts.Prompts.Count, Bench.SelectPrompts(Prompts, ["all"]).Count);

    [Fact]
    public void SelectPrompts_MatchesCaseInsensitivelyAndDeduplicates()
    {
        var selected = Bench.SelectPrompts(Prompts, ["ABSTENTION-ENCOURAGED", "abstention-encouraged"]);

        Assert.Equal("abstention-encouraged", selected.Single().Name);
    }

    /// Fail-closed: a typo must not silently fall back to the default and quietly change what the run
    /// measured.
    [Fact]
    public void SelectPrompts_UnknownName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Bench.SelectPrompts(Prompts, ["abstention-offerred"]));

        Assert.Contains("matches no prompt", ex.Message);
        Assert.Contains("abstention-offered", ex.Message);
    }

    /// The prompt is part of a live model's identity, because the number is a claim about the pair.
    [Fact]
    public void ALiveModelsNameCarriesItsPrompt()
    {
        var prompts = Bench.SelectPrompts(Prompts, ["all"]);
        var models = prompts.Select(p => new OllamaModel("llama3.2:3b", p)).ToList();

        Assert.Equal(
            [
                "llama3.2:3b @ abstention-offered",
                "llama3.2:3b @ abstention-unmentioned",
                "llama3.2:3b @ abstention-encouraged",
                "llama3.2:3b @ no-forced-choice"
            ],
            models.Select(m => m.Name));

        Assert.All(models, m => Assert.False(m.IsBaseline));
    }

    /// A fixture is untouched by --prompt: it is keyed on item id and never sees a system prompt.
    /// That is exactly why it is a reference point and not a competitor, and the report says so.
    [Fact]
    public void ABaselineNeverSeesASystemPrompt_WhicheverPromptWasSelected()
    {
        foreach (var model in Bench.LoadDemoModels(Bench.FindDataDir()))
        {
            Assert.True(model.IsBaseline);
            Assert.Null(model.SystemPrompt);
            Assert.Equal("none — a fixture never sees one", model.Provenance["systemPrompt"]);
        }
    }

    /// Sweeping a live model must produce one scorecard per prompt, not one overwritten by the last.
    [Fact]
    public void SelectModels_ByBareModelTag_SelectsEveryPromptVariantOfIt()
    {
        var prompts = Bench.SelectPrompts(Prompts, ["all"]);
        IReadOnlyList<IModel> available =
        [
            new ScriptedModel("CalibratedBaseline", new Dictionary<string, string>()),
            .. prompts.Select(p => new OllamaModel("llama3.2:3b", p))
        ];

        var swept = Bench.SelectModels(available, ["llama3.2:3b"]);
        Assert.Equal(prompts.Count, swept.Count);
        Assert.All(swept, m => Assert.StartsWith("llama3.2:3b @ ", m.Name));

        // ...and an exact name still selects exactly one.
        var one = Bench.SelectModels(available, ["llama3.2:3b @ abstention-encouraged"]);
        Assert.Equal("llama3.2:3b @ abstention-encouraged", one.Single().Name);
    }
}
