using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

/// The counterfactual arm exists to break one specific confound: on the Full + Ablated scorecard, a
/// model that reads the labs and is overconfident is INDISTINGUISHABLE from a model that ignores the
/// labs entirely and pattern-matches the shape of the vignette. Both score 100 % answer-accuracy and
/// 100 % unsupported answers. These tests pin that the third arm actually separates them.
public class CounterfactualTests
{
    private static readonly LexicalGrader Grader = LexicalGrader.Instance;

    private static Item Counterfactual(params string[] excluded)
        => new("c01", Variant.Counterfactual, "…", "INSUFFICIENT", MustAbstain: true,
            AcceptedAnswers: null, ExcludedAnswers: excluded);

    /// The targeted failure: the flipped finding says "not DKA", and the model says DKA anyway.
    [Fact]
    public void Score_NamingTheExcludedDiagnosis_IsEvidenceInsensitive()
    {
        var item = Counterfactual("Diabetic ketoacidosis", "DKA");

        Assert.Equal(Outcome.EvidenceInsensitive, Grader.Score(item, "Diabetic ketoacidosis."));
        Assert.Equal(Outcome.EvidenceInsensitive, Grader.Score(item, "DKA."));
    }

    [Fact]
    public void Score_DecliningOnACounterfactual_IsACorrectAbstention()
        => Assert.Equal(
            Outcome.CorrectAbstention,
            Grader.Score(Counterfactual("Diabetic ketoacidosis"), "INSUFFICIENT INFORMATION."));

    /// Naming a DIFFERENT diagnosis is still an unsupported answer — it over-reached — but it is NOT
    /// evidence-insensitive. The model plainly registered that the finding had changed.
    [Fact]
    public void Score_NamingADifferentDiagnosis_IsUnsupportedButNotEvidenceInsensitive()
        => Assert.Equal(
            Outcome.UnsupportedAnswer,
            Grader.Score(Counterfactual("Diabetic ketoacidosis", "DKA"), "Diabetes insipidus."));

    /// Order matters, and this is the case that proves it. A model that declines while mentioning the
    /// original diagnosis — to say the evidence now rules it out — has read the finding *perfectly*.
    /// Testing for the excluded answer before testing for abstention would convict it of
    /// evidence-insensitivity on the strength of the letters "DKA" appearing in the reply.
    [Theory]
    [InlineData("INSUFFICIENT - a normal glucose and negative ketones exclude DKA.")]
    [InlineData("INSUFFICIENT; the labs argue against diabetic ketoacidosis.")]
    [InlineData("Cannot determine. This is not diabetic ketoacidosis - the ketones are negative.")]
    public void Score_DecliningWhileNamingWhyTheOriginalIsExcluded_IsACorrectAbstention(string response)
        => Assert.Equal(
            Outcome.CorrectAbstention,
            Grader.Score(Counterfactual("Diabetic ketoacidosis", "DKA"), response));

    /// ...but hedging and then committing to the excluded diagnosis is still evidence-insensitive.
    [Fact]
    public void Score_HedgingThenNamingTheExcludedDiagnosis_IsEvidenceInsensitive()
        => Assert.Equal(
            Outcome.EvidenceInsensitive,
            Grader.Score(Counterfactual("Diabetic ketoacidosis", "DKA"), "Insufficient to be certain, but most likely DKA."));

    /// EvidenceInsensitive is only reachable on a counterfactual item. On an ablated item there is no
    /// contradicting finding to be insensitive TO — the evidence is merely absent.
    [Fact]
    public void Score_OnAnAblatedItem_TheOutcomeIsNeverEvidenceInsensitive()
    {
        var ablated = new Item("c01", Variant.Ablated, "…", "INSUFFICIENT", MustAbstain: true);

        Assert.Equal(Outcome.UnsupportedAnswer, Grader.Score(ablated, "Diabetic ketoacidosis."));
    }
}

/// What the arm buys, measured on the real fixtures.
public class CounterfactualScorecardTests
{
    private static Dictionary<string, Scorecard> RunAll()
    {
        var dataDir = Bench.FindDataDir();
        var items = Bench.ItemsFor(Bench.LoadCases(dataDir));

        return Bench.LoadDemoModels(dataDir)
            .Select(m => Scorecard.From(m.Name, Bench.RunModelAsync(m, items).GetAwaiter().GetResult()))
            .ToDictionary(c => c.ModelName);
    }

    [Fact]
    public void EveryCaseYieldsThreeItems()
    {
        var cases = Bench.LoadCases(Bench.FindDataDir());
        var items = Bench.ItemsFor(cases);

        Assert.Equal(cases.Count * 3, items.Count);
        Assert.Equal(cases.Count, items.Count(i => i.Variant == Variant.Counterfactual));
    }

    /// THE test. On the main scorecard AlwaysAnswerBaseline is a model that "knows the medicine": 100 %
    /// answer accuracy. The counterfactual arm reveals it was never reading the labs at all.
    [Fact]
    public void TheProbeSeparatesAModelThatReadsTheEvidenceFromOneThatDoesNot()
    {
        var cards = RunAll();
        var gestalt = cards["AlwaysAnswerBaseline"];
        var calibrated = cards["CalibratedBaseline"];

        // Indistinguishable on the thing the scorecard measures...
        Assert.Equal(calibrated.AnswerAccuracy.Value, gestalt.AnswerAccuracy.Value);

        // ...and utterly distinguishable once the finding is flipped to contradict.
        Assert.Equal(0.0, gestalt.EvidenceSensitivity.Value);
        Assert.Equal(1.0, gestalt.EvidenceInsensitivityRate.Value);
        Assert.Equal(1.0, calibrated.EvidenceSensitivity.Value);
        Assert.Equal(0.0, calibrated.EvidenceInsensitivityRate.Value);
    }

    /// Evidence-sensitivity, like abstention-recall, is trivially maximised by answering nothing.
    /// That is exactly why the arm is reported as a probe and not folded into selective accuracy.
    [Fact]
    public void EvidenceSensitivity_IsTriviallyMaximisedBySilence()
    {
        var abstain = RunAll()["AlwaysAbstainBaseline"];

        Assert.Equal(1.0, abstain.EvidenceSensitivity.Value);
        Assert.Equal(0.0, abstain.AnswerAccuracy.Value);
    }

    /// The counterfactual arm must NOT change what any pre-existing metric means. Twelve more
    /// must-abstain items would make the benchmark two-thirds abstention, and AlwaysAbstainBaseline
    /// would then BEAT AlwaysAnswerBaseline (24/36 vs 12/36) purely because silence had become the
    /// majority answer. Selective accuracy stays on the Full + Ablated arms, so the two degenerate
    /// poles still tie at 50 %.
    [Fact]
    public void SelectiveAccuracy_IgnoresTheCounterfactualArm_SoTheDegeneratePolesStillTie()
    {
        var cards = RunAll();

        Assert.Equal(24, cards["AlwaysAnswerBaseline"].SelectiveAccuracy.Total);
        Assert.Equal(0.5, cards["AlwaysAnswerBaseline"].SelectiveAccuracy.Value);
        Assert.Equal(0.5, cards["AlwaysAbstainBaseline"].SelectiveAccuracy.Value);
        Assert.Equal(1.0, cards["CalibratedBaseline"].SelectiveAccuracy.Value);

        // The other denominators are likewise untouched at 12.
        Assert.Equal(12, cards["CalibratedBaseline"].AblatedTotal);
        Assert.Equal(12, cards["CalibratedBaseline"].FullTotal);
        Assert.Equal(12, cards["CalibratedBaseline"].CounterfactualTotal);
    }
}

/// The counterfactual prompts are medical content. These tests check the *construction*, not the
/// medicine — the medicine is flagged in TASK.md as awaiting human review.
public class CounterfactualDataTests
{
    private static readonly List<BenchCase> Cases = Bench.LoadCases(Bench.FindDataDir());

    [Fact]
    public void EveryCase_HasACounterfactualPromptAndAnExplanationOfTheFlip()
    {
        foreach (var c in Cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.CounterfactualPrompt), $"{c.Id} has no counterfactual prompt");
            Assert.False(string.IsNullOrWhiteSpace(c.FlippedFact), $"{c.Id} does not say what was flipped");
            Assert.False(string.IsNullOrWhiteSpace(c.CounterfactualRationale), $"{c.Id} does not say why it is now under-determined");
        }
    }

    /// A counterfactual is not an ablation: the finding must still be PRESENT, only flipped. If the
    /// counterfactual prompt were just the ablated prompt, the arm would measure nothing new.
    [Fact]
    public void TheCounterfactualPromptIsDistinctFromBothOtherVariants()
    {
        foreach (var c in Cases)
        {
            Assert.NotEqual(c.FullPrompt, c.CounterfactualPrompt);
            Assert.NotEqual(c.AblatedPrompt, c.CounterfactualPrompt);

            // It restates a finding, so it is materially longer than the stripped-down ablated prompt.
            Assert.True(c.CounterfactualPrompt.Length > c.AblatedPrompt.Length,
                $"{c.Id}: the counterfactual prompt should carry a finding the ablated one lacks");
        }
    }

    /// The original diagnosis must never be the supported answer on its own counterfactual item.
    [Fact]
    public void NamingTheOriginalDiagnosis_IsEvidenceInsensitiveOnEveryCase()
    {
        foreach (var c in Cases)
        {
            var counterfactual = Item.FromCase(c).Single(i => i.Variant == Variant.Counterfactual);

            Assert.Equal(Outcome.EvidenceInsensitive, LexicalGrader.Instance.Score(counterfactual, c.ExpectedAnswer + "."));

            foreach (var synonym in c.AcceptedAnswers ?? [])
                Assert.Equal(Outcome.EvidenceInsensitive, LexicalGrader.Instance.Score(counterfactual, synonym + "."));
        }
    }
}
