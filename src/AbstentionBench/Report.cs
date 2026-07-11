using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicalAbstentionBench;

/// The JSON report. Aggregates alone are not auditable — a scorecard says a model produced an
/// unsupported answer on 12 of 12 items but not *what it said*, so the claim cannot be checked and
/// cannot be reproduced. Every run therefore carries the full per-item transcript plus enough
/// provenance (when, which endpoint, which weights, which system prompt) to re-run it.
public static class Report
{
    /// Written into every report so a stale artifact can be told apart from a current one.
    /// Bumped to 3: every rate is now an object ({value, successes, total, ci95}) where it used to be
    /// a bare number. That is a breaking change for anything reading the aggregates.
    public const string SchemaVersion = "3";

    public static RunReport Build(
        string mode,
        int caseCount,
        int itemCount,
        IReadOnlyList<IModel> models,
        IReadOnlyDictionary<string, IReadOnlyList<ItemResult>> resultsByModel,
        IReadOnlyList<Scorecard> cards,
        DateTimeOffset timestamp,
        IGrader? grader = null)
    {
        grader ??= LexicalGrader.Instance;

        var provenance = new RunProvenance(
            SchemaVersion,
            timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            mode,
            grader.Name,
            [.. models.Select(m => new ModelProvenance(
                m.Name,
                m.SystemPrompt,
                new SortedDictionary<string, string>(m.Provenance.ToDictionary(kv => kv.Key, kv => kv.Value))))]);

        var transcripts = new List<TranscriptEntry>(itemCount * models.Count);
        foreach (var model in models)
        {
            if (!resultsByModel.TryGetValue(model.Name, out var results))
                throw new InvalidOperationException($"No results recorded for model '{model.Name}' — refusing to write a partial report.");

            transcripts.AddRange(results.Select(r => new TranscriptEntry(
                r.ModelName,
                r.Item.CaseId,
                r.Item.VariantName,
                r.Item.Key,
                r.SystemPrompt,
                r.Item.Prompt,
                r.Response,
                r.Outcome.ToString(),
                r.Item.GroundTruth)));
        }

        return new RunReport(provenance, caseCount, itemCount, [.. cards.Select(ModelScores.From)], transcripts);
    }

    public static void Write(string path, RunReport report)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(report));
    }

    public static string Serialize(RunReport report) => JsonSerializer.Serialize(report, Options);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public sealed record RunReport(
    RunProvenance Provenance,
    int Cases,
    int Items,
    IReadOnlyList<ModelScores> Models,
    IReadOnlyList<TranscriptEntry> Transcripts);

public sealed record RunProvenance(
    string SchemaVersion,
    string TimestampUtc,
    string Mode,
    /// Which grader turned replies into outcomes. A score is only meaningful next to this.
    string Grader,
    IReadOnlyList<ModelProvenance> Models);

public sealed record ModelProvenance(
    string Name,
    string? SystemPrompt,
    IReadOnlyDictionary<string, string> Details);

/// One model, one item, one reply, verbatim — the row a reader checks when they doubt a number.
public sealed record TranscriptEntry(
    string Model,
    string CaseId,
    string Variant,
    string ItemKey,
    string? SystemPrompt,
    string Prompt,
    string Response,
    string Outcome,
    string SupportedAnswer);

/// The aggregate half of the report. Every rate ships with the counts behind it and its 95 % Wilson
/// interval, so a consumer cannot read a point estimate without also being handed its precision.
public sealed record ModelScores(
    string ModelName,
    ReportedRate AbstentionRecall,
    ReportedRate UnsupportedAnswerRate,
    ReportedRate AnswerAccuracy,
    ReportedRate OverAbstentionRate,
    ReportedRate SelectiveAccuracy,
    int AblatedTotal,
    int CorrectAbstentions,
    int UnsupportedAnswers,
    int FullTotal,
    int CorrectAnswers,
    int WrongAnswers,
    int OverAbstentions)
{
    public static ModelScores From(Scorecard c) => new(
        c.ModelName,
        ReportedRate.From(c.AbstentionRecall),
        ReportedRate.From(c.UnsupportedAnswerRate),
        ReportedRate.From(c.AnswerAccuracy),
        ReportedRate.From(c.OverAbstentionRate),
        ReportedRate.From(c.SelectiveAccuracy),
        c.AblatedTotal, c.CorrectAbstentions, c.UnsupportedAnswers,
        c.FullTotal, c.CorrectAnswers, c.WrongAnswers, c.OverAbstentions);
}

/// A rate as it appears in JSON: the point estimate, the counts it came from, and the 95 % Wilson
/// score interval. `ci95` is [lower, upper].
public sealed record ReportedRate(double Value, int Successes, int Total, IReadOnlyList<double> Ci95)
{
    public static ReportedRate From(Rate r) => new(r.Value, r.Successes, r.Total, [r.Lower, r.Upper]);
}
