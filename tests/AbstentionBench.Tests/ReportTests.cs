using System.Text.Json;
using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ReportTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 7, 11, 9, 30, 0, TimeSpan.Zero);

    private static RunReport BuildRealRun()
    {
        var dataDir = Bench.FindDataDir();
        var cases = Bench.LoadCases(dataDir);
        var items = Bench.ItemsFor(cases);
        var models = Bench.LoadDemoModels(dataDir);

        var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>();
        var cards = new List<Scorecard>();
        foreach (var m in models)
        {
            var results = Bench.RunModelAsync(m, items).GetAwaiter().GetResult();
            resultsByModel[m.Name] = results;
            cards.Add(Scorecard.From(m.Name, results));
        }

        return Report.Build("demo", cases.Count, items.Count, models, resultsByModel, cards, FixedTime);
    }

    [Fact]
    public void Build_EmitsOneTranscriptPerItemPerModel()
    {
        var report = BuildRealRun();

        Assert.Equal(report.Items * report.Models.Count, report.Transcripts.Count);
        Assert.Equal(report.Transcripts.Count, report.Transcripts.Select(t => $"{t.Model}/{t.ItemKey}").Distinct().Count());
    }

    /// The whole point of the transcript: a reader who doubts a score can see the exact prompt sent
    /// and the exact reply that produced it, without re-running anything.
    [Fact]
    public void Build_TranscriptIsSelfContained_PromptResponseAndOutcome()
    {
        var report = BuildRealRun();
        var entry = report.Transcripts.Single(t => t is { Model: "AlwaysAnswerBaseline", ItemKey: "c01:ablated" });

        Assert.Equal("c01", entry.CaseId);
        Assert.Equal("ablated", entry.Variant);
        Assert.Contains("19-year-old", entry.Prompt);
        Assert.DoesNotContain("512", entry.Prompt); // the decisive finding really is gone
        Assert.Equal("Diabetic ketoacidosis.", entry.Response);
        Assert.Equal(nameof(Outcome.UnsupportedAnswer), entry.Outcome);
        Assert.Equal("INSUFFICIENT", entry.SupportedAnswer);
    }

    [Fact]
    public void Build_RecordsProvenance_TimestampModeAndPerModelDetails()
    {
        var report = BuildRealRun();

        Assert.Equal(Report.SchemaVersion, report.Provenance.SchemaVersion);
        Assert.Equal("2026-07-11T09:30:00Z", report.Provenance.TimestampUtc);
        Assert.Equal("demo", report.Provenance.Mode);

        var scripted = report.Provenance.Models.Single(m => m.Name == "CalibratedBaseline");
        Assert.Equal("scripted-fixture", scripted.Details["kind"]);
        Assert.Null(scripted.SystemPrompt); // fixtures never see one — the README's caveat, enforced
    }

    /// A live model's system prompt is the single biggest confound on an abstention number, so it
    /// must land in the transcript rather than being implicit in the source.
    [Fact]
    public void Build_RecordsTheSystemPromptInForce_ForALiveModel()
    {
        var item = new Item("c01", Variant.Full, "prompt?", "Flu", MustAbstain: false);
        var model = new OllamaModel("llama3.2:3b");
        var results = new List<ItemResult>
        {
            new(model.Name, item, model.SystemPrompt, "Flu.", Outcome.CorrectAnswer)
        };

        var report = Report.Build(
            "ollama", 1, 1, [model],
            new Dictionary<string, IReadOnlyList<ItemResult>> { [model.Name] = results },
            [Scorecard.From(model.Name, results)],
            FixedTime);

        Assert.Equal(OllamaModel.DefaultSystemPrompt, report.Transcripts[0].SystemPrompt);
        Assert.Equal(OllamaModel.DefaultSystemPrompt, report.Provenance.Models[0].SystemPrompt);
        Assert.Equal("ollama", report.Provenance.Models[0].Details["kind"]);
        Assert.Equal("0", report.Provenance.Models[0].Details["temperature"]);
    }

    /// Fail-closed: a report is never written from partial results.
    [Fact]
    public void Build_MissingResultsForAModel_Throws()
    {
        var model = new ScriptedModel("Ghost", new Dictionary<string, string>());

        var ex = Assert.Throws<InvalidOperationException>(() => Report.Build(
            "demo", 0, 0, [model],
            new Dictionary<string, IReadOnlyList<ItemResult>>(),
            [],
            FixedTime));

        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void Serialize_RoundTripsAndKeepsAggregatesAlongsideTranscripts()
    {
        var json = Report.Serialize(BuildRealRun());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var models = root.GetProperty("models").GetArrayLength();
        Assert.Equal(12, root.GetProperty("cases").GetInt32());
        Assert.Equal(24, root.GetProperty("items").GetInt32());
        Assert.Equal(24 * models, root.GetProperty("transcripts").GetArrayLength());

        var always = root.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("modelName").GetString() == "AlwaysAnswerBaseline");

        // Every rate ships as {value, successes, total, ci95} — a consumer cannot read the point
        // estimate without being handed its precision alongside it.
        var unsupported = always.GetProperty("unsupportedAnswerRate");
        Assert.Equal(1.0, unsupported.GetProperty("value").GetDouble());
        Assert.Equal(12, unsupported.GetProperty("successes").GetInt32());
        Assert.Equal(12, unsupported.GetProperty("total").GetInt32());

        var ci = unsupported.GetProperty("ci95");
        Assert.Equal(2, ci.GetArrayLength());
        Assert.Equal(0.7575, ci[0].GetDouble(), precision: 4);
        Assert.Equal(1.0, ci[1].GetDouble());
    }

    [Fact]
    public void Write_CreatesTheDirectoryAndTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cab-{Guid.NewGuid():N}", "results", "run.json");
        try
        {
            Report.Write(path, BuildRealRun());

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
