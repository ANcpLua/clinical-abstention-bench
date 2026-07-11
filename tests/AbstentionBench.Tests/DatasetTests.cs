using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

/// <summary>
/// Contract tests over the repository dataset itself. These intentionally exercise the same files
/// loaded by the executable; no second miniature case inventory is maintained in the test project.
/// </summary>
public class DatasetTests
{
    [Fact]
    public void RepositoryContainsTwelveCasesAndExactlyThreeEvidenceStatesPerCase()
    {
        Assert.Equal(12, RepositoryBenchmark.Cases.Count);
        Assert.Equal(36, RepositoryBenchmark.Items.Count);

        foreach (var benchmarkCase in RepositoryBenchmark.Cases)
        {
            var items = Item.FromCase(benchmarkCase).ToList();

            Assert.Collection(
                items,
                full =>
                {
                    Assert.Equal(Variant.Full, full.Variant);
                    Assert.Equal(EvidenceRelation.OriginalSupported, full.Relation);
                    Assert.Equal($"{benchmarkCase.Id}:full", full.Key);
                },
                ablated =>
                {
                    Assert.Equal(Variant.Ablated, ablated.Variant);
                    Assert.Equal(EvidenceRelation.EvidenceAblated, ablated.Relation);
                    Assert.Equal($"{benchmarkCase.Id}:ablated", ablated.Key);
                },
                contrast =>
                {
                    Assert.Equal(Variant.Contrast, contrast.Variant);
                    Assert.Equal(EvidenceRelation.AlternativeSupported, contrast.Relation);
                    Assert.Equal($"{benchmarkCase.Id}:contrast", contrast.Key);
                });
        }
    }

    [Fact]
    public void EveryTargetIsInternallyConsistentAndReferencesTheConceptCatalog()
    {
        foreach (var item in RepositoryBenchmark.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Vignette), $"{item.Key} has no vignette");
            Assert.True(
                RepositoryBenchmark.Catalog.Contains(item.OriginalConcept),
                $"{item.Key} has unknown original concept {item.OriginalConcept}");

            if (item.Target.Diagnosis is null)
            {
                Assert.Equal(DiagnosticStatus.Indeterminate, item.Target.DiagnosticStatus);
                Assert.Empty(item.Target.AcceptedConcepts);
                Assert.Empty(item.Target.AcceptedParentConcepts ?? []);
                continue;
            }

            Assert.NotEqual(DiagnosticStatus.Indeterminate, item.Target.DiagnosticStatus);
            Assert.Equal(item.Target.Diagnosis, item.Target.AllAcceptedConcepts[0]);
            foreach (var concept in item.Target.AllAcceptedConcepts.Concat(item.Target.AcceptedParentConcepts ?? []))
                Assert.True(RepositoryBenchmark.Catalog.Contains(concept), $"{item.Key} has unknown target concept {concept}");
        }
    }

    [Fact]
    public void NullTargetCountsComeFromTargets_NotFromVariantNames()
    {
        var standard = RepositoryBenchmark.Items
            .Where(item => item.Variant is Variant.Full or Variant.Ablated)
            .ToList();
        var nullTargets = standard.Where(item => item.Target.Diagnosis is null).ToList();
        var card = RepositoryBenchmark.Scorecards()[RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle).Name];

        Assert.NotEmpty(nullTargets);
        Assert.All(nullTargets, item => Assert.Equal(Variant.Ablated, item.Variant));
        Assert.True(
            nullTargets.Count < RepositoryBenchmark.Cases.Count,
            "Every ablation was incorrectly treated as a mandatory deferral.");
        Assert.Equal(nullTargets.Count, card.StandardTargetNull);
        Assert.Equal(standard.Count - nullTargets.Count, card.StandardTargetNonNull);
    }

    [Fact]
    public void C08AndC12AblationsSupportProbableDiagnosesRatherThanMandatoryDeferral()
    {
        var cystitis = RepositoryBenchmark.Item("c08", Variant.Ablated).Target;
        var hypoglycemia = RepositoryBenchmark.Item("c12", Variant.Ablated).Target;

        Assert.Equal("acute_cystitis", cystitis.Diagnosis);
        Assert.Equal(DiagnosticStatus.Probable, cystitis.DiagnosticStatus);
        Assert.Equal(Urgency.Routine, cystitis.Urgency);

        Assert.Equal("hypoglycemia", hypoglycemia.Diagnosis);
        Assert.Equal(DiagnosticStatus.Probable, hypoglycemia.DiagnosticStatus);
        Assert.Equal(Urgency.Emergency, hypoglycemia.Urgency);
    }

    [Fact]
    public void ReviewedHighRiskAblationsRetainWorkingDiagnosesAndIndependentEmergencyUrgency()
    {
        var meningitis = RepositoryBenchmark.Item("c02", Variant.Ablated).Target;
        var acuteCoronarySyndrome = RepositoryBenchmark.Item("c03", Variant.Ablated).Target;
        var gout = RepositoryBenchmark.Item("c06", Variant.Ablated).Target;

        Assert.Equal(("meningitis", DiagnosticStatus.Probable, Urgency.Emergency),
            (meningitis.Diagnosis, meningitis.DiagnosticStatus, meningitis.Urgency));
        Assert.Equal(("acute_coronary_syndrome", DiagnosticStatus.Probable, Urgency.Emergency),
            (acuteCoronarySyndrome.Diagnosis, acuteCoronarySyndrome.DiagnosticStatus, acuteCoronarySyndrome.Urgency));
        Assert.Equal(("gout", DiagnosticStatus.Probable, Urgency.Emergency),
            (gout.Diagnosis, gout.DiagnosticStatus, gout.Urgency));
    }

    [Fact]
    public void NonspecificC10AndC11AblationsDeferDiagnosisWithoutDeferringCare()
    {
        var abdominalPain = RepositoryBenchmark.Item("c10", Variant.Ablated).Target;
        var suddenHeadache = RepositoryBenchmark.Item("c11", Variant.Ablated).Target;

        Assert.Null(abdominalPain.Diagnosis);
        Assert.Equal(DiagnosticStatus.Indeterminate, abdominalPain.DiagnosticStatus);
        Assert.Equal(Urgency.Urgent, abdominalPain.Urgency);

        Assert.Null(suddenHeadache.Diagnosis);
        Assert.Equal(DiagnosticStatus.Indeterminate, suddenHeadache.DiagnosticStatus);
        Assert.Equal(Urgency.Emergency, suddenHeadache.Urgency);
    }

    [Fact]
    public void IndeterminateDiagnosisNeverErasesUrgentOrEmergencyAction()
    {
        var indeterminate = RepositoryBenchmark.Items
            .Where(item => item.Target.Diagnosis is null)
            .ToList();

        Assert.Contains(indeterminate, item => item.Target.Urgency == Urgency.Emergency);
        Assert.Contains(indeterminate, item => item.Target.Urgency == Urgency.Urgent);
        Assert.All(indeterminate, item => Assert.NotEqual(DiagnosticStatus.Probable, item.Target.DiagnosticStatus));
    }

    [Fact]
    public void ContrastsSupportExplicitAlternativesAndNeverReuseTheOriginalConcept()
    {
        var contrasts = RepositoryBenchmark.Items.Where(item => item.Variant == Variant.Contrast).ToList();

        Assert.Equal(RepositoryBenchmark.Cases.Count, contrasts.Count);
        Assert.All(contrasts, item =>
        {
            Assert.NotNull(item.Target.Diagnosis);
            Assert.NotEqual(item.OriginalConcept, item.Target.Diagnosis);
            Assert.NotEqual(DiagnosticStatus.Indeterminate, item.Target.DiagnosticStatus);
        });
    }
}
