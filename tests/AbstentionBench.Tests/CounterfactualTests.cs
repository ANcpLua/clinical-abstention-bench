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

    private static Item Counterfactual() => RepositoryBenchmark.Item("c01", Variant.Counterfactual);

    /// The targeted failure: the flipped finding says "not DKA", and the model says DKA anyway.
    [Fact]
    public void Score_NamingTheExcludedDiagnosis_IsEvidenceInsensitive()
    {
        var item = Counterfactual();

        Assert.Equal(Outcome.EvidenceInsensitive, Grader.Score(item, "Diabetic ketoacidosis."));
        Assert.Equal(Outcome.EvidenceInsensitive, Grader.Score(item, "DKA."));
    }

    [Fact]
    public void Score_DecliningOnACounterfactual_IsACorrectAbstention()
        => Assert.Equal(
            Outcome.CorrectAbstention,
            Grader.Score(Counterfactual(), "INSUFFICIENT INFORMATION."));

    /// Naming a DIFFERENT diagnosis is still an unsupported answer — it over-reached — but it is NOT
    /// evidence-insensitive. The model plainly registered that the finding had changed.
    [Fact]
    public void Score_NamingADifferentDiagnosis_IsUnsupportedButNotEvidenceInsensitive()
        => Assert.Equal(
            Outcome.UnsupportedAnswer,
            Grader.Score(Counterfactual(), "Diabetes insipidus."));

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
            Grader.Score(Counterfactual(), response));

    /// ...but hedging and then committing to the excluded diagnosis is still evidence-insensitive.
    [Fact]
    public void Score_HedgingThenNamingTheExcludedDiagnosis_IsEvidenceInsensitive()
        => Assert.Equal(
            Outcome.EvidenceInsensitive,
            Grader.Score(Counterfactual(), "Insufficient to be certain, but most likely DKA."));

    /// EvidenceInsensitive is only reachable on a counterfactual item. On an ablated item there is no
    /// contradicting finding to be insensitive TO — the evidence is merely absent.
    [Fact]
    public void Score_OnAnAblatedItem_TheOutcomeIsNeverEvidenceInsensitive()
    {
        var ablated = RepositoryBenchmark.Item("c01", Variant.Ablated);

        Assert.Equal(Outcome.UnsupportedAnswer, Grader.Score(ablated, "Diabetic ketoacidosis."));
    }

    /// A normal ECG excludes ST-elevation MI, not every form of acute coronary syndrome. This exact
    /// NSTEMI response was previously misclassified as evidence-insensitive because the full-item
    /// accepted-answer list was incorrectly reused as the counterfactual exclusion list.
    [Theory]
    [InlineData("Acute myocardial infarction.")]
    [InlineData("Myocardial infarction.")]
    [InlineData("Acute coronary syndrome (ACS) - likely non-ST-elevation myocardial infarction (NSTEMI).")]
    public void C03_GenericMiOrNstemi_IsUnsupportedButNotEvidenceInsensitive(string response)
    {
        var counterfactual = RepositoryBenchmark.Item("c03", Variant.Counterfactual);

        Assert.Equal(Outcome.UnsupportedAnswer, Grader.Score(counterfactual, response));
    }

    [Theory]
    [InlineData("STEMI.")]
    [InlineData("ST-elevation myocardial infarction.")]
    [InlineData("Inferior wall STEMI.")]
    public void C03_Stemi_RemainsEvidenceInsensitive(string response)
    {
        var counterfactual = RepositoryBenchmark.Item("c03", Variant.Counterfactual);

        Assert.Equal(Outcome.EvidenceInsensitive, Grader.Score(counterfactual, response));
    }
}

/// What the arm buys, measured on the repository cases and programmatic reference policies.
public class CounterfactualScorecardTests
{
    private static Dictionary<string, Scorecard> RunAll() => RepositoryBenchmark.Scorecards();

    private static Scorecard Card(IReadOnlyDictionary<string, Scorecard> cards, ReferencePolicy policy)
        => cards[RepositoryBenchmark.Policy(policy).Name];

    [Fact]
    public void EveryCaseYieldsThreeItems()
    {
        var cases = RepositoryBenchmark.Cases;
        var items = RepositoryBenchmark.Items;

        Assert.Equal(cases.Count * 3, items.Count);
        Assert.Equal(cases.Count, items.Count(i => i.Variant == Variant.Counterfactual));
    }

    /// On the main scorecard the AlwaysAnswer policy is correct on every Full label. The
    /// counterfactual arm separates that degenerate policy from the configured scoring target.
    [Fact]
    public void TheProbeSeparatesAlwaysAnswerFromTheLabelOracle()
    {
        var cards = RunAll();
        var alwaysAnswer = Card(cards, ReferencePolicy.AlwaysAnswer);
        var labelOracle = Card(cards, ReferencePolicy.LabelOracle);

        // Indistinguishable on the thing the scorecard measures...
        Assert.Equal(labelOracle.AnswerAccuracy.Value, alwaysAnswer.AnswerAccuracy.Value);

        // ...and utterly distinguishable once the finding is flipped to contradict.
        Assert.Equal(0.0, alwaysAnswer.EvidenceSensitivity.Value);
        Assert.Equal(1.0, alwaysAnswer.EvidenceInsensitivityRate.Value);
        Assert.Equal(1.0, labelOracle.EvidenceSensitivity.Value);
        Assert.Equal(0.0, labelOracle.EvidenceInsensitivityRate.Value);
    }

    /// Evidence-sensitivity, like abstention-recall, is trivially maximised by answering nothing.
    /// That is exactly why the arm is reported as a probe and not folded into selective accuracy.
    [Fact]
    public void EvidenceSensitivity_IsTriviallyMaximisedBySilence()
    {
        var cards = RunAll();
        var abstain = Card(cards, ReferencePolicy.AlwaysAbstain);

        Assert.Equal(1.0, abstain.EvidenceSensitivity.Value);
        Assert.Equal(0.0, abstain.AnswerAccuracy.Value);
    }

    /// The counterfactual arm must NOT change what any pre-existing metric means. Adding one more
    /// must-abstain item per case would make silence the majority answer if it were folded into the
    /// score. Selective accuracy stays on the Full + Ablated arms, so the two degenerate poles still
    /// tie at 50 %.
    [Fact]
    public void SelectiveAccuracy_IgnoresTheCounterfactualArm_SoTheDegeneratePolesStillTie()
    {
        var cards = RunAll();
        var caseCount = RepositoryBenchmark.Cases.Count;
        var alwaysAnswer = Card(cards, ReferencePolicy.AlwaysAnswer);
        var alwaysAbstain = Card(cards, ReferencePolicy.AlwaysAbstain);
        var labelOracle = Card(cards, ReferencePolicy.LabelOracle);

        Assert.Equal(caseCount * 2, alwaysAnswer.SelectiveAccuracy.Total);
        Assert.Equal(0.5, alwaysAnswer.SelectiveAccuracy.Value);
        Assert.Equal(0.5, alwaysAbstain.SelectiveAccuracy.Value);
        Assert.Equal(1.0, labelOracle.SelectiveAccuracy.Value);

        Assert.Equal(caseCount, labelOracle.AblatedTotal);
        Assert.Equal(caseCount, labelOracle.FullTotal);
        Assert.Equal(caseCount, labelOracle.CounterfactualTotal);
    }
}

/// The counterfactual prompts are medical content. These tests check the *construction*, not the
/// medicine — the medicine is flagged in TASK.md as awaiting human review.
public class CounterfactualDataTests
{
    private static readonly IReadOnlyList<BenchCase> Cases = RepositoryBenchmark.Cases;

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

    /// A counterfactual cannot be identical to either existing arm. This is a construction-level
    /// identity guard only; whether the changed finding is medically valid remains human-reviewed.
    [Fact]
    public void TheCounterfactualPromptIsDistinctFromBothOtherVariants()
    {
        foreach (var c in Cases)
        {
            Assert.NotEqual(c.FullPrompt, c.CounterfactualPrompt);
            Assert.NotEqual(c.AblatedPrompt, c.CounterfactualPrompt);
        }
    }

    /// Every configured counterfactual exclusion must reach the evidence-insensitive outcome. This
    /// checks the data-to-item construction; clinical validity remains a separate review question.
    [Fact]
    public void EveryConfiguredCounterfactualExclusion_IsEvidenceInsensitive()
    {
        foreach (var c in Cases)
        {
            var counterfactual = RepositoryBenchmark.Item(c.Id, Variant.Counterfactual);

            Assert.NotEmpty(counterfactual.ExcludedForms);
            foreach (var excluded in counterfactual.ExcludedForms)
                Assert.Equal(Outcome.EvidenceInsensitive, LexicalGrader.Instance.Score(counterfactual, excluded + "."));
        }
    }
}
