using System.Text.Json;
using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ResultArtifactTests
{
    private static readonly string ResultsDirectory = Path.GetFullPath(
        Path.Combine(RepositoryBenchmark.DataDirectory, "..", "results"));

    [Fact]
    public void EveryCurrentV2ArtifactReplaysAgainstTheCurrentRepositoryContract()
    {
        var currentArtifacts = Directory
            .EnumerateFiles(ResultsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(IsSchemaV5)
            .ToList();

        // A repository may temporarily contain no fresh observations during a contract migration.
        // When a v2 artifact is tracked, however, every grade and aggregate must be reproducible.
        foreach (var path in currentArtifacts)
            Replay(path);
    }

    [Fact]
    public void LegacyArtifactsRemainParseableFrozenObservations_NotV2ReplayFixtures()
    {
        var legacyDirectory = Path.Combine(ResultsDirectory, "legacy-v1");
        var legacyArtifacts = Directory.Exists(legacyDirectory)
            ? Directory.EnumerateFiles(legacyDirectory, "*.json", SearchOption.TopDirectoryOnly).ToList()
            : [];

        Assert.NotEmpty(legacyArtifacts);
        foreach (var path in legacyArtifacts)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var schema = document.RootElement.GetProperty("provenance").GetProperty("schemaVersion").GetString();

            Assert.NotEqual(Report.SchemaVersion, schema);
            Assert.True(document.RootElement.TryGetProperty("transcripts", out _));
            // Deliberately do not feed these historical raw responses through the v2 grader: their
            // prompts, targets, outcome taxonomy, and aggregate denominators belong to schema v4.
        }
    }

    private static bool IsSchemaV5(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("provenance").GetProperty("schemaVersion").GetString()
               == Report.SchemaVersion;
    }

    private static void Replay(string path)
    {
        var json = File.ReadAllText(path);
        var report = JsonSerializer.Deserialize<RunReport>(json, Bench.Json)
                     ?? throw new InvalidDataException($"{path} did not deserialize as a v2 report.");
        var items = RepositoryBenchmark.Items.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var resultsByModel = new Dictionary<string, List<ItemResult>>(StringComparer.Ordinal);

        Assert.Equal(Report.SchemaVersion, report.Provenance.SchemaVersion);
        Assert.Equal(RepositoryBenchmark.Cases.Count, report.Cases);
        Assert.Equal(RepositoryBenchmark.Items.Count, report.Items);

        foreach (var transcript in report.Transcripts)
        {
            var item = items[transcript.ItemKey];
            var grade = RepositoryBenchmark.Grader.Score(item, transcript.RawResponse);
            var profile = ProfileFor(report, transcript.Model);

            Assert.Equal(item.CaseId, transcript.CaseId);
            Assert.Equal(item.VariantName, transcript.Variant);
            Assert.Equal(item.Relation.WireName(), transcript.Relation);
            Assert.Equal(item.OriginalConcept, transcript.OriginalConcept);
            Assert.Equal(profile.RenderUserPrompt(item.Vignette), transcript.SentPrompt);
            Assert.Equal(item.Target.Diagnosis, transcript.Target.Diagnosis);
            Assert.Equal(item.Target.DiagnosticStatus.WireName(), transcript.Target.DiagnosticStatus);
            Assert.Equal(item.Target.AllAcceptedConcepts, transcript.Target.AcceptedConcepts);
            Assert.Equal(item.Target.AcceptedParentConcepts ?? [], transcript.Target.AcceptedParentConcepts);
            Assert.Equal(item.Target.Urgency.WireName(), transcript.Target.Urgency);

            Assert.Equal(grade.Response.Diagnosis, transcript.ParsedResponse.Diagnosis);
            Assert.Equal(grade.Response.Certainty.WireName(), transcript.ParsedResponse.Certainty);
            Assert.Equal(grade.Response.Urgency.WireName(), transcript.ParsedResponse.Urgency);
            Assert.Equal(grade.DiagnosisOutcome.WireName(), transcript.Grade.DiagnosisOutcome);
            Assert.Equal(grade.ResolvedConcept, transcript.Grade.ResolvedConcept);
            Assert.Equal(grade.AcceptedAsParentConcept, transcript.Grade.AcceptedAsParentConcept);
            Assert.Equal(grade.CertaintyCorrect, transcript.Grade.CertaintyCorrect);
            Assert.Equal(grade.UrgencyCorrect, transcript.Grade.UrgencyCorrect);
            Assert.Equal(grade.Undertriage, transcript.Grade.Undertriage);

            if (!resultsByModel.TryGetValue(transcript.Model, out var results))
            {
                results = [];
                resultsByModel.Add(transcript.Model, results);
            }
            results.Add(new ItemResult(
                transcript.Model,
                item,
                transcript.SystemPrompt,
                transcript.SentPrompt,
                transcript.RawResponse,
                grade));
        }

        Assert.Equal(report.Models.Select(model => model.ModelName).Order(), resultsByModel.Keys.Order());
        foreach (var reported in report.Models)
        {
            var replayed = ModelScores.From(Scorecard.From(reported.ModelName, resultsByModel[reported.ModelName]));
            Assert.Equal(JsonSerializer.Serialize(replayed), JsonSerializer.Serialize(reported));
        }
    }

    private static PromptProfile ProfileFor(RunReport report, string modelName)
    {
        var provenance = report.Provenance.Models.Single(model => model.Name == modelName);
        if (provenance.Details.TryGetValue("promptName", out var promptName))
            return report.Provenance.Prompts.Single(prompt => prompt.Name == promptName);

        return report.Provenance.Prompts.Single(prompt => prompt.Canonical);
    }
}
