using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class PromptProfileTests
{
    private const string LiveModelTag = "llama3.2:3b";

    [Fact]
    public void RepositoryDefinesOneCanonicalEvidenceProfileAndOneForcedChoiceStressArm()
    {
        var file = RepositoryBenchmark.PromptProfiles;

        Assert.Equal("evidence-required", file.Default);
        Assert.Equal(["evidence-required", "forced-choice"], file.Prompts.Select(profile => profile.Name));
        Assert.Same(RepositoryBenchmark.CanonicalProfile, file.Prompts.Single(profile => profile.Canonical));

        var evidence = file.Prompts.Single(profile => profile.Name == "evidence-required");
        var forced = file.Prompts.Single(profile => profile.Name == "forced-choice");
        Assert.DoesNotContain("single most likely", evidence.SystemText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("single most likely", evidence.UserTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single most likely", forced.UserTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-null", forced.SystemText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachProfileOwnsBothMessagesAndRendersTheVignetteExactlyOnce()
    {
        const string vignette = "A repository-backed vignette.";

        foreach (var profile in RepositoryBenchmark.PromptProfiles.Prompts)
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.SystemText));
            Assert.False(string.IsNullOrWhiteSpace(profile.UserTemplate));
            Assert.Equal(
                1,
                profile.UserTemplate.Split(PromptProfile.VignetteToken, StringSplitOptions.None).Length - 1);

            var rendered = profile.RenderUserPrompt(vignette);
            Assert.Contains(vignette, rendered);
            Assert.DoesNotContain(PromptProfile.VignetteToken, rendered);
            Assert.Equal(1, rendered.Split(vignette, StringSplitOptions.None).Length - 1);
        }

        var profiles = RepositoryBenchmark.PromptProfiles.Prompts;
        Assert.Equal(profiles.Count, profiles.Select(profile => profile.SystemText).Distinct().Count());
        Assert.Equal(profiles.Count, profiles.Select(profile => profile.UserTemplate).Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenderRejectsAnEmptyVignette(string vignette)
        => Assert.Throws<InvalidDataException>(
            () => RepositoryBenchmark.CanonicalProfile.RenderUserPrompt(vignette));

    [Fact]
    public void SelectionDefaultsToCanonicalAndAllSweepsBothCompleteContracts()
    {
        var file = RepositoryBenchmark.PromptProfiles;

        Assert.Same(
            RepositoryBenchmark.CanonicalProfile,
            Assert.Single(Bench.SelectPromptProfiles(file, [])));
        Assert.Equal(file.Prompts, Bench.SelectPromptProfiles(file, ["all"]));
        Assert.Same(
            file.Prompts[1],
            Assert.Single(Bench.SelectPromptProfiles(file, [file.Prompts[1].Name.ToUpperInvariant(), file.Prompts[1].Name])));
    }

    [Fact]
    public void SelectionFailsClosedOnUnknownProfile()
    {
        var unknown = RepositoryBenchmark.PromptProfiles.Default + "-typo";
        var ex = Assert.Throws<InvalidOperationException>(
            () => Bench.SelectPromptProfiles(RepositoryBenchmark.PromptProfiles, [unknown]));

        Assert.Contains(unknown, ex.Message);
        Assert.Contains(RepositoryBenchmark.PromptProfiles.Default, ex.Message);
    }

    [Fact]
    public void LiveModelIdentityAndProvenanceCarryItsCompletePromptProfile()
    {
        foreach (var profile in RepositoryBenchmark.PromptProfiles.Prompts)
        {
            var model = new OllamaModel(LiveModelTag, profile);

            Assert.Equal($"{LiveModelTag} @ {profile.Name}", model.Name);
            Assert.Equal(profile.SystemText, model.SystemPrompt);
            Assert.Same(profile, model.PromptProfile);
            Assert.Equal(profile.SystemText, model.Provenance["systemPrompt"]);
            Assert.Equal(profile.UserTemplate, model.Provenance["userTemplate"]);
            Assert.Equal(profile.Name, model.Provenance["promptName"]);
            Assert.False(model.IsBaseline);
        }
    }

    [Fact]
    public void BareLiveModelNameSelectsEveryPromptArmButExactNameSelectsOne()
    {
        IReadOnlyList<IModel> available =
        [
            .. RepositoryBenchmark.ReferenceModels,
            .. RepositoryBenchmark.PromptProfiles.Prompts.Select(profile => new OllamaModel(LiveModelTag, profile))
        ];

        var sweep = Bench.SelectModels(available, [LiveModelTag]);
        Assert.Equal(RepositoryBenchmark.PromptProfiles.Prompts.Count, sweep.Count);

        var exact = $"{LiveModelTag} @ {RepositoryBenchmark.CanonicalProfile.Name}";
        Assert.Equal(exact, Assert.Single(Bench.SelectModels(available, [exact])).Name);
    }
}
