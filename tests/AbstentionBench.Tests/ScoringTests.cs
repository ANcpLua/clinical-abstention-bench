using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScoringTests
{
    [Fact]
    public void StandardMetricsSeparateCoverageSelectiveAccuracyAndDecisionAccuracy()
    {
        const string model = "metric-contract";
        var correct = RepositoryBenchmark.Item("c01", Variant.Full);
        var wrong = RepositoryBenchmark.Item("c02", Variant.Full);
        var deferred = RepositoryBenchmark.Items.First(item =>
            item.Variant == Variant.Ablated && item.Target.Diagnosis is null);

        var results = new[]
        {
            RepositoryBenchmark.Grade(correct, RepositoryBenchmark.TargetResponse(correct), model),
            RepositoryBenchmark.Grade(
                wrong,
                new ModelResponse(correct.Target.Diagnosis, wrong.Target.DiagnosticStatus, wrong.Target.Urgency),
                model),
            RepositoryBenchmark.Grade(deferred, RepositoryBenchmark.TargetResponse(deferred), model)
        };

        var card = Scorecard.From(model, results);

        Assert.Equal(new Rate(2, 3), card.Coverage);
        Assert.Equal(new Rate(1, 2), card.SelectiveAccuracy);
        Assert.Equal(new Rate(1, 2), card.SelectiveRisk);
        Assert.Equal(new Rate(2, 3), card.DecisionAccuracy);
        Assert.Equal(1, card.StandardCorrectDiagnoses);
        Assert.Equal(1, card.StandardWrongDiagnoses);
        Assert.Equal(1, card.StandardCorrectDeferrals);
    }

    [Fact]
    public void AbstentionMetricsUseOnlyActuallyNullTargets()
    {
        var oracle = RepositoryBenchmark.Scorecards()[RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle).Name];
        var standard = RepositoryBenchmark.Items
            .Where(item => item.Variant is Variant.Full or Variant.Ablated)
            .ToList();
        var targetNull = standard.Count(item => !item.Target.HasDiagnosis);

        Assert.Equal(targetNull, oracle.StandardTargetNull);
        Assert.Equal(targetNull, oracle.AbstentionRecall.Total);
        Assert.Equal(standard.Count - targetNull, oracle.StandardTargetNonNull);
        Assert.DoesNotContain(
            RepositoryBenchmark.Item("c08", Variant.Ablated),
            standard.Where(item => !item.Target.HasDiagnosis));
        Assert.DoesNotContain(
            RepositoryBenchmark.Item("c12", Variant.Ablated),
            standard.Where(item => !item.Target.HasDiagnosis));
    }

    [Fact]
    public void ContrastMetricsMeasureAccuracyOriginalPersistenceAndPairedRevisionSeparately()
    {
        const string model = "paired-contract";
        var revisedCase = RepositoryBenchmark.Case("c01");
        var persistentCase = RepositoryBenchmark.Case("c02");
        var revisedFull = RepositoryBenchmark.Item(revisedCase.Id, Variant.Full);
        var revisedContrast = RepositoryBenchmark.Item(revisedCase.Id, Variant.Contrast);
        var persistentFull = RepositoryBenchmark.Item(persistentCase.Id, Variant.Full);
        var persistentContrast = RepositoryBenchmark.Item(persistentCase.Id, Variant.Contrast);

        var results = new[]
        {
            RepositoryBenchmark.Grade(revisedFull, RepositoryBenchmark.TargetResponse(revisedFull), model),
            RepositoryBenchmark.Grade(revisedContrast, RepositoryBenchmark.TargetResponse(revisedContrast), model),
            RepositoryBenchmark.Grade(persistentFull, RepositoryBenchmark.TargetResponse(persistentFull), model),
            RepositoryBenchmark.Grade(
                persistentContrast,
                new ModelResponse(
                    persistentContrast.OriginalConcept,
                    persistentContrast.Target.DiagnosticStatus,
                    persistentContrast.Target.Urgency),
                model)
        };

        var card = Scorecard.From(model, results);

        Assert.Equal(new Rate(1, 2), card.ContrastAccuracy);
        Assert.Equal(new Rate(1, 2), card.OriginalTargetPersistence);
        Assert.Equal(new Rate(1, 2), card.PairedRevisionAccuracy);
        Assert.Equal(2, card.PairedTotal);
        Assert.Equal(1, card.PairedRevisionCorrect);
    }

    [Fact]
    public void OriginalPersistenceIncludesOriginalOnlyParentsButNotParentsSharedWithTheContrast()
    {
        const string model = "parent-persistence";
        var full = RepositoryBenchmark.Item("c03", Variant.Full);
        var contrast = RepositoryBenchmark.Item("c03", Variant.Contrast);
        var fullResult = RepositoryBenchmark.Grade(full, RepositoryBenchmark.TargetResponse(full), model);

        var stillStemi = RepositoryBenchmark.Grade(
            contrast,
            new ModelResponse("stemi", contrast.Target.DiagnosticStatus, contrast.Target.Urgency),
            model);
        var sharedAcuteMi = RepositoryBenchmark.Grade(
            contrast,
            new ModelResponse(
                "acute_myocardial_infarction",
                contrast.Target.DiagnosticStatus,
                contrast.Target.Urgency),
            model);

        Assert.Equal(
            new Rate(1, 1),
            Scorecard.From(model, [fullResult, stillStemi]).OriginalTargetPersistence);
        Assert.Equal(
            new Rate(0, 1),
            Scorecard.From(model, [fullResult, sharedAcuteMi]).OriginalTargetPersistence);
        Assert.Equal(
            new Rate(0, 1),
            Scorecard.From(model, [fullResult, sharedAcuteMi]).PairedRevisionAccuracy);
    }

    [Fact]
    public void ContrastArmNeverChangesPrimaryMetricDenominators()
    {
        const string model = "arm-boundary";
        var full = RepositoryBenchmark.Item("c01", Variant.Full);
        var ablated = RepositoryBenchmark.Item("c01", Variant.Ablated);
        var contrast = RepositoryBenchmark.Item("c01", Variant.Contrast);
        var primary = new[]
        {
            RepositoryBenchmark.Grade(full, RepositoryBenchmark.TargetResponse(full), model),
            RepositoryBenchmark.Grade(ablated, RepositoryBenchmark.TargetResponse(ablated), model)
        };
        var withoutContrast = Scorecard.From(model, primary);
        var withContrast = Scorecard.From(
            model,
            [.. primary, RepositoryBenchmark.Grade(contrast, RepositoryBenchmark.TargetResponse(contrast), model)]);

        Assert.Equal(withoutContrast.Coverage, withContrast.Coverage);
        Assert.Equal(withoutContrast.SelectiveAccuracy, withContrast.SelectiveAccuracy);
        Assert.Equal(withoutContrast.DecisionAccuracy, withContrast.DecisionAccuracy);
        Assert.Equal(0, withoutContrast.ContrastTotal);
        Assert.Equal(1, withContrast.ContrastTotal);
    }

    [Fact]
    public void ScorecardRejectsDuplicateItemsAndMixedModelNames()
    {
        var item = RepositoryBenchmark.Item("c01", Variant.Full);
        var first = RepositoryBenchmark.Grade(item, RepositoryBenchmark.TargetResponse(item), "first");
        var second = RepositoryBenchmark.Grade(item, RepositoryBenchmark.TargetResponse(item), "second");

        Assert.Throws<InvalidDataException>(() => Scorecard.From("first", [first, first]));
        Assert.Throws<InvalidDataException>(() => Scorecard.From("first", [first, second]));
    }
}
