using System.Text.Json;
using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ReportTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 7, 11, 9, 30, 0, TimeSpan.Zero);

    private static async Task<RunReport> BuildRepositoryRunAsync()
    {
        var models = RepositoryBenchmark.ReferenceModels;
        var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>(StringComparer.Ordinal);
        var cards = new List<Scorecard>();

        foreach (var model in models)
        {
            var results = await RepositoryBenchmark.Run(model);
            resultsByModel[model.Name] = results;
            cards.Add(Scorecard.From(model.Name, results));
        }

        return Report.Build(
            "demo",
            RepositoryBenchmark.Cases.Count,
            RepositoryBenchmark.Items.Count,
            models,
            resultsByModel,
            cards,
            FixedTime,
            RepositoryBenchmark.Grader,
            [RepositoryBenchmark.CanonicalProfile]);
    }

    [Fact]
    public async Task ReportContainsOneSelfContainedTranscriptPerModelAndItem()
    {
        var report = await BuildRepositoryRunAsync();
        var model = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var item = RepositoryBenchmark.Item("c12", Variant.Ablated);
        var transcript = report.Transcripts.Single(entry =>
            entry.Model == model.Name && entry.ItemKey == item.Key);

        Assert.Equal(report.Items * report.Models.Count, report.Transcripts.Count);
        Assert.Equal(
            report.Transcripts.Count,
            report.Transcripts.Select(entry => $"{entry.Model}/{entry.ItemKey}").Distinct().Count());
        Assert.Equal(item.Relation.WireName(), transcript.Relation);
        Assert.Equal(item.OriginalConcept, transcript.OriginalConcept);
        Assert.Equal(RepositoryBenchmark.CanonicalProfile.RenderUserPrompt(item.Vignette), transcript.SentPrompt);
        Assert.Equal(item.Target.Diagnosis, transcript.Target.Diagnosis);
        Assert.Equal(item.Target.DiagnosticStatus.WireName(), transcript.Target.DiagnosticStatus);
        Assert.Equal(item.Target.Urgency.WireName(), transcript.Target.Urgency);
        Assert.Equal(DiagnosisOutcome.CorrectDiagnosis.WireName(), transcript.Grade.DiagnosisOutcome);
        Assert.Equal(item.OriginalConcept, transcript.ParsedResponse.Diagnosis);
        Assert.Null(transcript.SystemPrompt);
    }

    [Fact]
    public async Task ReportRecordsSchemaGraderPromptsAndReferencePolicyProvenance()
    {
        var report = await BuildRepositoryRunAsync();

        Assert.Equal("5", Report.SchemaVersion);
        Assert.Equal(Report.SchemaVersion, report.Provenance.SchemaVersion);
        Assert.Equal("structured-concept-v2", report.Provenance.Grader);
        Assert.Equal("2026-07-11T09:30:00Z", report.Provenance.TimestampUtc);
        Assert.Equal("demo", report.Provenance.Mode);
        Assert.Equal(RepositoryBenchmark.CanonicalProfile, Assert.Single(report.Provenance.Prompts));

        var oracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var provenance = report.Provenance.Models.Single(model => model.Name == oracle.Name);
        Assert.True(provenance.IsBaseline);
        Assert.Null(provenance.SystemPrompt);
        Assert.Equal("deterministic-reference-policy", provenance.Details["kind"]);
        Assert.Contains("target diagnosis", provenance.Details["labelAccess"]);
    }

    [Fact]
    public void LiveModelReportCarriesBothPromptMessagesAndTheRenderedUserMessage()
    {
        var profile = RepositoryBenchmark.PromptProfiles.Prompts.Single(prompt => prompt.Name == "forced-choice");
        var model = new OllamaModel("llama3.2:3b", profile);
        var item = RepositoryBenchmark.Item("c01", Variant.Full);
        var result = RepositoryBenchmark.Grade(item, RepositoryBenchmark.TargetResponse(item), model.Name) with
        {
            SystemPrompt = model.SystemPrompt,
            SentPrompt = profile.RenderUserPrompt(item.Vignette)
        };
        var report = Report.Build(
            "ollama",
            1,
            1,
            [model],
            new Dictionary<string, IReadOnlyList<ItemResult>> { [model.Name] = [result] },
            [Scorecard.From(model.Name, [result])],
            FixedTime,
            RepositoryBenchmark.Grader,
            [profile]);

        Assert.Equal(profile.SystemText, report.Provenance.Prompts.Single().SystemText);
        Assert.Equal(profile.UserTemplate, report.Provenance.Prompts.Single().UserTemplate);
        Assert.Equal(profile.SystemText, report.Provenance.Models.Single().SystemPrompt);
        Assert.Equal(profile.SystemText, report.Transcripts.Single().SystemPrompt);
        Assert.Equal(profile.RenderUserPrompt(item.Vignette), report.Transcripts.Single().SentPrompt);
    }

    [Fact]
    public void ReportRefusesPartialModelResults()
    {
        var model = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAbstain);

        var ex = Assert.Throws<InvalidOperationException>(() => Report.Build(
            "demo",
            RepositoryBenchmark.Cases.Count,
            RepositoryBenchmark.Items.Count,
            [model],
            new Dictionary<string, IReadOnlyList<ItemResult>>(),
            [],
            FixedTime,
            RepositoryBenchmark.Grader));

        Assert.Contains(model.Name, ex.Message);
    }

    [Fact]
    public async Task SerializationRoundTripsRatesCountsAndWilsonIntervals()
    {
        var report = await BuildRepositoryRunAsync();
        var json = Report.Serialize(report);
        var roundTrip = JsonSerializer.Deserialize<RunReport>(json, Bench.Json);

        Assert.NotNull(roundTrip);
        Assert.Equal(json, Report.Serialize(roundTrip));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(RepositoryBenchmark.Cases.Count, root.GetProperty("cases").GetInt32());
        Assert.Equal(RepositoryBenchmark.Items.Count, root.GetProperty("items").GetInt32());
        var firstRate = root.GetProperty("models")[0].GetProperty("selectiveAccuracy");
        Assert.True(firstRate.TryGetProperty("value", out _));
        Assert.True(firstRate.TryGetProperty("successes", out _));
        Assert.True(firstRate.TryGetProperty("total", out _));
        Assert.Equal(2, firstRate.GetProperty("ci95").GetArrayLength());
    }

    [Fact]
    public async Task WriteCreatesTheRequestedDirectoryAndFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cab-{Guid.NewGuid():N}", "results", "run.json");
        try
        {
            Report.Write(path, await BuildRepositoryRunAsync());

            Assert.True(File.Exists(path));
            Assert.Contains("parsedResponse", File.ReadAllText(path));
        }
        finally
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
