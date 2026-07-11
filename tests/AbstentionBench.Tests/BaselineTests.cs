using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ReferencePolicyTests
{
    [Fact]
    public async Task EveryPolicyProducesStrictStructuredResponsesForEveryRepositoryItem()
    {
        foreach (var model in RepositoryBenchmark.ReferenceModels)
        {
            var results = await RepositoryBenchmark.Run(model);

            Assert.Equal(RepositoryBenchmark.Items.Count, results.Count);
            Assert.All(results, result =>
            {
                Assert.False(string.IsNullOrWhiteSpace(result.RawResponse));
                _ = StructuredConceptGrader.ParseResponse(result.RawResponse, result.Item.Key);
                Assert.Null(result.SystemPrompt);
                Assert.Equal(
                    RepositoryBenchmark.CanonicalProfile.RenderUserPrompt(result.Item.Vignette),
                    result.SentPrompt);
            });
        }
    }

    [Fact]
    public async Task PoliciesFollowTheirDeclaredProgrammaticRules()
    {
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var alwaysAbstain = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAbstain);
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);

        var answered = await RepositoryBenchmark.Run(alwaysAnswer);
        var abstained = await RepositoryBenchmark.Run(alwaysAbstain);
        var oracle = await RepositoryBenchmark.Run(labelOracle);

        Assert.All(answered, result =>
        {
            Assert.Equal(result.Item.OriginalConcept, result.Grade.Response.Diagnosis);
            Assert.Equal(DiagnosticStatus.Established, result.Grade.Response.Certainty);
            Assert.Equal(Urgency.Routine, result.Grade.Response.Urgency);
        });
        Assert.All(abstained, result =>
        {
            Assert.Null(result.Grade.Response.Diagnosis);
            Assert.Equal(DiagnosticStatus.Indeterminate, result.Grade.Response.Certainty);
            Assert.Equal(Urgency.Routine, result.Grade.Response.Urgency);
        });
        Assert.All(oracle, result =>
        {
            Assert.Equal(result.Item.Target.Diagnosis, result.Grade.Response.Diagnosis);
            Assert.Equal(result.Item.Target.DiagnosticStatus, result.Grade.Response.Certainty);
            Assert.Equal(result.Item.Target.Urgency, result.Grade.Response.Urgency);
            Assert.True(result.Grade.DiagnosisDecisionCorrect);
            Assert.True(result.Grade.CertaintyCorrect);
            Assert.True(result.Grade.UrgencyCorrect);
        });
    }

    [Fact]
    public void ReferencePoliciesDeclareLabelAccessAndNeverPretendToBeLiveModels()
    {
        foreach (var model in RepositoryBenchmark.ReferenceModels.Cast<ReferencePolicyModel>())
        {
            Assert.True(model.IsBaseline);
            Assert.Null(((IModel)model).SystemPrompt);
            Assert.Equal("deterministic-reference-policy", model.Provenance["kind"]);
            Assert.Equal(model.Policy.ToString(), model.Provenance["policy"]);
            Assert.True(model.Provenance.ContainsKey("labelAccess"));
            Assert.Equal("none — reference policies never see one", model.Provenance["systemPrompt"]);
        }
    }

    [Fact]
    public void LabelOracleIsPerfectButAlwaysAbstainIsPenalizedOnSupportedTargets()
    {
        var cards = RepositoryBenchmark.Scorecards();
        var oracle = cards[RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle).Name];
        var abstain = cards[RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAbstain).Name];

        Assert.Equal(1.0, oracle.SelectiveAccuracy.Value);
        Assert.Equal(1.0, oracle.DecisionAccuracy.Value);
        Assert.Equal(1.0, oracle.ContrastAccuracy.Value);
        Assert.Equal(1.0, oracle.PairedRevisionAccuracy.Value);
        Assert.Equal(1.0, oracle.CertaintyAccuracy.Value);
        Assert.Equal(1.0, oracle.UrgencyAccuracy.Value);

        Assert.Equal(0.0, abstain.Coverage.Value);
        Assert.Equal(1.0, abstain.OverabstentionRate.Value);
        Assert.Equal(0.0, abstain.ContrastAccuracy.Value);
        Assert.True(abstain.DecisionAccuracy.Value < oracle.DecisionAccuracy.Value);
    }
}
