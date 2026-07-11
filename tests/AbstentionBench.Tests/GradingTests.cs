using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ConceptCatalogTests
{
    [Fact]
    public void RepositoryFormsResolveCaseInsensitivelyButOnlyAsWholeFields()
    {
        var expected = RepositoryBenchmark.Catalog.Resolve("  hYpOgLyCaEmIa  ");

        Assert.NotNull(expected);
        Assert.Equal("hypoglycemia", expected.ConceptId);
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("hypoglycemia with seizure"));
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("probable hypoglycemia"));
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("not hypoglycemia"));
    }

    [Fact]
    public void CatalogRejectsAFormSharedByDifferentConcepts()
    {
        var existing = RepositoryBenchmark.Concepts[0];
        var collision = new DiagnosticConcept(
            existing.Id + "_collision",
            "Unambiguous preferred name",
            [existing.PreferredName.ToUpperInvariant()]);

        var ex = Assert.Throws<InvalidDataException>(
            () => new ConceptCatalog([.. RepositoryBenchmark.Concepts, collision]));

        Assert.Contains("ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(existing.Id, ex.Message);
        Assert.Contains(collision.Id, ex.Message);
    }

    [Fact]
    public void RepositoryCatalogDoesNotEncodeEtiologiesOrManifestationsTheEvidenceNeverEstablished()
    {
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("hypoglycemic seizure"));
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("hypoglycemic encephalopathy"));
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("meningococcal meningitis"));
        Assert.Null(RepositoryBenchmark.Catalog.Resolve("ruptured berry aneurysm"));
    }
}

public class StructuredConceptGraderTests
{
    private static string Response(
        string? diagnosis,
        DiagnosticStatus certainty,
        Urgency urgency)
        => StructuredResponseJson.Serialize(new ModelResponse(diagnosis, certainty, urgency));

    [Fact]
    public void ExactTargetAndExplicitParentAreAcceptedWithoutSubstringInference()
    {
        var item = RepositoryBenchmark.Item("c03", Variant.Full);
        var exact = RepositoryBenchmark.Grader.Score(
            item,
            Response(item.Target.Diagnosis, item.Target.DiagnosticStatus, item.Target.Urgency));
        var parentId = item.Target.AcceptedParentConcepts!.First();
        var parent = RepositoryBenchmark.Grader.Score(
            item,
            Response(parentId, item.Target.DiagnosticStatus, item.Target.Urgency));

        Assert.Equal(DiagnosisOutcome.CorrectDiagnosis, exact.DiagnosisOutcome);
        Assert.False(exact.AcceptedAsParentConcept);
        Assert.Equal(DiagnosisOutcome.CorrectDiagnosis, parent.DiagnosisOutcome);
        Assert.True(parent.AcceptedAsParentConcept);
    }

    [Fact]
    public void InventedSpecificityIsWrongEvenWhenItContainsTheRightWords()
    {
        var item = RepositoryBenchmark.Item("c12", Variant.Full);
        var grade = RepositoryBenchmark.Grader.Score(
            item,
            Response("hypoglycemic seizure", item.Target.DiagnosticStatus, item.Target.Urgency));

        Assert.Equal(DiagnosisOutcome.WrongDiagnosis, grade.DiagnosisOutcome);
        Assert.Null(grade.ResolvedConcept);
        Assert.True(grade.Answered);
    }

    [Fact]
    public void DiagnosticDecisionCertaintyAndUrgencyAreScoredIndependently()
    {
        var item = RepositoryBenchmark.Item("c12", Variant.Ablated);
        var grade = RepositoryBenchmark.Grader.Score(
            item,
            Response(item.Target.Diagnosis, DiagnosticStatus.Established, Urgency.Routine));

        Assert.Equal(DiagnosisOutcome.CorrectDiagnosis, grade.DiagnosisOutcome);
        Assert.False(grade.CertaintyCorrect);
        Assert.False(grade.UrgencyCorrect);
        Assert.True(grade.Undertriage);
    }

    [Fact]
    public void NullAndNonNullTargetsProduceDistinctDeferralFailures()
    {
        var nullTarget = RepositoryBenchmark.Items.First(item => item.Target.Diagnosis is null);
        var nonNullTarget = RepositoryBenchmark.Item("c08", Variant.Ablated);

        var correctDeferral = RepositoryBenchmark.Grader.Score(
            nullTarget,
            Response(null, DiagnosticStatus.Indeterminate, nullTarget.Target.Urgency));
        var unsupported = RepositoryBenchmark.Grader.Score(
            nullTarget,
            Response(nullTarget.OriginalConcept, DiagnosticStatus.Probable, nullTarget.Target.Urgency));
        var overabstention = RepositoryBenchmark.Grader.Score(
            nonNullTarget,
            Response(null, DiagnosticStatus.Indeterminate, nonNullTarget.Target.Urgency));

        Assert.Equal(DiagnosisOutcome.CorrectDeferral, correctDeferral.DiagnosisOutcome);
        Assert.Equal(DiagnosisOutcome.UnsupportedDiagnosis, unsupported.DiagnosisOutcome);
        Assert.Equal(DiagnosisOutcome.OverAbstention, overabstention.DiagnosisOutcome);
    }

    [Fact]
    public void GraderRejectsAnInvalidNullTargetInsteadOfNormalizingIt()
    {
        var source = RepositoryBenchmark.Items.First(item => item.Target.Diagnosis is null);
        var invalidTarget = source.Target with
        {
            AcceptedConcepts = [source.OriginalConcept]
        };
        var invalidItem = source with
        {
            CaseVariant = source.CaseVariant with { Target = invalidTarget }
        };

        Assert.Throws<InvalidDataException>(() => RepositoryBenchmark.Grader.Score(
            invalidItem,
            Response(null, DiagnosticStatus.Indeterminate, invalidTarget.Urgency)));
    }
}

public class StructuredResponseContractTests
{
    [Fact]
    public void ParserAcceptsExactlyTheThreeFieldsInAnyOrder()
    {
        const string raw = """
            {"urgency":"emergency","diagnosis":"  Hypoglycemia  ","certainty":"probable"}
            """;

        var parsed = StructuredConceptGrader.ParseResponse(raw, "contract-test");

        Assert.Equal("Hypoglycemia", parsed.Diagnosis);
        Assert.Equal(DiagnosticStatus.Probable, parsed.Certainty);
        Assert.Equal(Urgency.Emergency, parsed.Urgency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("```json\n{\"diagnosis\":null,\"certainty\":\"indeterminate\",\"urgency\":\"urgent\"}\n```")]
    [InlineData("{\"diagnosis\":null,\"certainty\":\"indeterminate\",\"urgency\":\"urgent\",\"rationale\":\"text\"}")]
    [InlineData("{\"diagnosis\":null,\"certainty\":\"indeterminate\"}")]
    [InlineData("{\"diagnosis\":null,\"diagnosis\":null,\"certainty\":\"indeterminate\",\"urgency\":\"urgent\"}")]
    [InlineData("{\"diagnosis\":\"  \",\"certainty\":\"probable\",\"urgency\":\"urgent\"}")]
    [InlineData("{\"diagnosis\":3,\"certainty\":\"probable\",\"urgency\":\"urgent\"}")]
    [InlineData("{\"diagnosis\":null,\"certainty\":\"uncertain\",\"urgency\":\"urgent\"}")]
    [InlineData("{\"diagnosis\":null,\"certainty\":\"indeterminate\",\"urgency\":\"soon\"}")]
    [InlineData("{\"diagnosis\":null,\"certainty\":\"indeterminate\",\"urgency\":\"urgent\",}")]
    [InlineData("[null,\"indeterminate\",\"urgent\"]")]
    public void ParserRejectsAnythingOutsideTheStrictContract(string raw)
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => StructuredConceptGrader.ParseResponse(raw, "strict-item"));

        Assert.Contains("strict-item", ex.Message);
    }
}
