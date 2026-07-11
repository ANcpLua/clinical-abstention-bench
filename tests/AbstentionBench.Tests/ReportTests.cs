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

        var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>();
        var cards = new List<Scorecard>();
        foreach (var m in models)
        {
            var results = await Bench.RunModelAsync(m, RepositoryBenchmark.Items);
            resultsByModel[m.Name] = results;
            cards.Add(Scorecard.From(m.Name, results));
        }

        return Report.Build(
            "demo",
            RepositoryBenchmark.Cases.Count,
            RepositoryBenchmark.Items.Count,
            models,
            resultsByModel,
            cards,
            FixedTime);
    }

    [Fact]
    public async Task Build_EmitsOneTranscriptPerItemPerModel()
    {
        var report = await BuildRepositoryRunAsync();

        Assert.Equal(report.Items * report.Models.Count, report.Transcripts.Count);
        Assert.Equal(report.Transcripts.Count, report.Transcripts.Select(t => $"{t.Model}/{t.ItemKey}").Distinct().Count());
    }

    /// The whole point of the transcript: a reader who doubts a score can see the exact prompt sent
    /// and the exact reply that produced it, without re-running anything.
    [Fact]
    public async Task Build_TranscriptIsSelfContained_PromptResponseAndOutcome()
    {
        var model = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var item = RepositoryBenchmark.Items.First(i => i.Variant == Variant.Ablated);
        var response = await model.AnswerAsync(new ModelInput(item.Key, item.Prompt));
        var expectedOutcome = LexicalGrader.Instance.Score(item, response);

        var report = await BuildRepositoryRunAsync();
        var entry = report.Transcripts.Single(t => t.Model == model.Name && t.ItemKey == item.Key);

        Assert.Equal(Outcome.UnsupportedAnswer, expectedOutcome);
        Assert.Equal(item.CaseId, entry.CaseId);
        Assert.Equal(item.VariantName, entry.Variant);
        Assert.Equal(item.Prompt, entry.Prompt);
        Assert.Equal(response, entry.Response);
        Assert.Equal(expectedOutcome.ToString(), entry.Outcome);
        Assert.Equal(item.GroundTruth, entry.SupportedAnswer);
    }

    [Fact]
    public async Task Build_RecordsProvenance_TimestampModeAndPerModelDetails()
    {
        var report = await BuildRepositoryRunAsync();

        Assert.Equal(Report.SchemaVersion, report.Provenance.SchemaVersion);
        Assert.Equal("2026-07-11T09:30:00Z", report.Provenance.TimestampUtc);
        Assert.Equal("demo", report.Provenance.Mode);

        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var reference = report.Provenance.Models.Single(m => m.Name == labelOracle.Name);
        Assert.Equal("deterministic-reference-policy", reference.Details["kind"]);
        Assert.Equal(ReferencePolicy.LabelOracle.ToString(), reference.Details["policy"]);
        Assert.Null(reference.SystemPrompt); // reference policies never see one
    }

    /// A live model's system prompt is the single biggest confound on an abstention number, so it must
    /// land in the transcript rather than being implicit in the source — and the model's own name
    /// carries the prompt, so no row can be read without knowing which prompt produced it.
    [Fact]
    public void Build_RecordsTheSystemPromptInForce_ForALiveModel()
    {
        var prompts = Bench.LoadPrompts(RepositoryBenchmark.DataDirectory);
        var prompt = Bench.SelectPrompts(prompts, []).Single();

        var item = RepositoryBenchmark.Items.First(i => i.Variant == Variant.Full);
        const string modelTag = "llama3.2:3b";
        var model = new OllamaModel(modelTag, prompt);
        var response = item.GroundTruth + ".";
        var results = new List<ItemResult>
        {
            new(model.Name, item, model.SystemPrompt, response, LexicalGrader.Instance.Score(item, response))
        };
        var caseCount = results.Select(result => result.Item.CaseId).Distinct().Count();

        var report = Report.Build(
            "ollama", caseCount, results.Count, [model],
            new Dictionary<string, IReadOnlyList<ItemResult>> { [model.Name] = results },
            [Scorecard.From(model.Name, results)],
            FixedTime,
            prompts: [prompt]);

        Assert.Equal($"{modelTag} @ {prompt.Name}", model.Name);
        Assert.Equal(prompt.Text, report.Transcripts[0].SystemPrompt);
        Assert.Equal(prompt.Text, report.Provenance.Models[0].SystemPrompt);
        Assert.False(report.Provenance.Models[0].IsBaseline);
        Assert.Equal("ollama", report.Provenance.Models[0].Details["kind"]);
        Assert.Equal("0", report.Provenance.Models[0].Details["temperature"]);
        Assert.Equal(prompt.Name, report.Provenance.Models[0].Details["promptName"]);

        // The prompt text travels with the run, verbatim.
        Assert.Equal(prompt.Text, report.Provenance.Prompts.Single().Text);
    }

    /// Fail-closed: a report is never written from partial results.
    [Fact]
    public void Build_MissingResultsForAModel_Throws()
    {
        var model = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAbstain);

        var ex = Assert.Throws<InvalidOperationException>(() => Report.Build(
            "demo", RepositoryBenchmark.Cases.Count, RepositoryBenchmark.Items.Count, [model],
            new Dictionary<string, IReadOnlyList<ItemResult>>(),
            [],
            FixedTime));

        Assert.Contains(model.Name, ex.Message);
    }

    [Fact]
    public async Task Serialize_RoundTripsAndKeepsAggregatesAlongsideTranscripts()
    {
        var report = await BuildRepositoryRunAsync();
        var json = Report.Serialize(report);
        var roundTripped = JsonSerializer.Deserialize<RunReport>(json, Bench.Json);

        Assert.NotNull(roundTripped);
        Assert.Equal(json, Report.Serialize(roundTripped));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var models = root.GetProperty("models").GetArrayLength();
        var items = root.GetProperty("items").GetInt32();
        Assert.Equal(RepositoryBenchmark.Cases.Count, root.GetProperty("cases").GetInt32());
        Assert.Equal(RepositoryBenchmark.Items.Count, items);
        Assert.Equal(items * models, root.GetProperty("transcripts").GetArrayLength());

        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var expected = report.Models.Single(m => m.ModelName == alwaysAnswer.Name).UnsupportedAnswerRate;
        var always = root.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("modelName").GetString() == alwaysAnswer.Name);

        // Every rate ships as {value, successes, total, ci95} — a consumer cannot read the point
        // estimate without being handed its precision alongside it.
        var unsupported = always.GetProperty("unsupportedAnswerRate");
        Assert.Equal(expected.Value, unsupported.GetProperty("value").GetDouble());
        Assert.Equal(expected.Successes, unsupported.GetProperty("successes").GetInt32());
        Assert.Equal(expected.Total, unsupported.GetProperty("total").GetInt32());

        var ci = unsupported.GetProperty("ci95");
        Assert.Equal(2, ci.GetArrayLength());
        Assert.Equal(expected.Ci95[0], ci[0].GetDouble());
        Assert.Equal(expected.Ci95[1], ci[1].GetDouble());
    }

    [Fact]
    public async Task Write_CreatesTheDirectoryAndTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cab-{Guid.NewGuid():N}", "results", "run.json");
        try
        {
            Report.Write(path, await BuildRepositoryRunAsync());

            Assert.True(File.Exists(path));
            Assert.Contains("transcripts", File.ReadAllText(path));
        }
        finally
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
