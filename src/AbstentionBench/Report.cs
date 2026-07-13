using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicalAbstentionBench;

/// Auditable JSON report for structured diagnostic selectivity.
public static class Report
{
    public static RunReport Build(
        string mode,
        int caseCount,
        int itemCount,
        IReadOnlyList<IModel> models,
        IReadOnlyDictionary<string, IReadOnlyList<ItemResult>> resultsByModel,
        IReadOnlyList<Scorecard> cards,
        DateTimeOffset timestamp,
        IGrader grader,
        IReadOnlyList<PromptProfile>? prompts = null)
    {
        var provenance = new RunProvenance(
            timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            mode,
            grader.Name,
            prompts ?? [],
            [.. models.Select(model => new ModelProvenance(
                model.Name,
                model.IsBaseline,
                model.SystemPrompt,
                new SortedDictionary<string, string>(model.Provenance.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value))))]);

        var transcripts = new List<TranscriptEntry>(itemCount * models.Count);
        foreach (var model in models)
        {
            if (!resultsByModel.TryGetValue(model.Name, out var results))
                throw new InvalidOperationException(
                    $"No results recorded for model '{model.Name}' — refusing to write a partial report.");

            transcripts.AddRange(results.Select(ToTranscript));
        }

        return new RunReport(
            provenance,
            caseCount,
            itemCount,
            [.. cards.Select(ModelScores.From)],
            transcripts);
    }

    public static void Write(string path, RunReport report)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(report));
    }

    public static string Serialize(RunReport report) => JsonSerializer.Serialize(report, Options);

    private static TranscriptEntry ToTranscript(ItemResult result)
    {
        var target = result.Item.Target;
        var grade = result.Grade;

        return new TranscriptEntry(
            result.ModelName,
            result.Item.CaseId,
            result.Item.VariantName,
            result.Item.Key,
            result.Item.Relation.WireName(),
            result.Item.OriginalConcept,
            result.SystemPrompt,
            result.SentPrompt,
            result.RawResponse,
            new TranscriptResponse(
                grade.Response.Diagnosis,
                grade.Response.Certainty.WireName(),
                grade.Response.Urgency.WireName()),
            new TranscriptTarget(
                target.Diagnosis,
                target.DiagnosticStatus.WireName(),
                target.AllAcceptedConcepts,
                target.AcceptedParentConcepts ?? [],
                target.Urgency.WireName()),
            new TranscriptGrade(
                grade.DiagnosisOutcome.WireName(),
                grade.ResolvedConcept,
                grade.AcceptedAsParentConcept,
                grade.CertaintyCorrect,
                grade.UrgencyCorrect,
                grade.Undertriage));
    }

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
    string TimestampUtc,
    string Mode,
    string Grader,
    /// Complete prompt profiles, including both the system message and user template.
    IReadOnlyList<PromptProfile> Prompts,
    IReadOnlyList<ModelProvenance> Models);

public sealed record ModelProvenance(
    string Name,
    bool IsBaseline,
    string? SystemPrompt,
    IReadOnlyDictionary<string, string> Details);

/// One self-contained audit row. `SentPrompt` is the actual rendered user message, not a value
/// reconstructed from current case or profile data.
public sealed record TranscriptEntry(
    string Model,
    string CaseId,
    string Variant,
    string ItemKey,
    string Relation,
    string OriginalConcept,
    string? SystemPrompt,
    string SentPrompt,
    string RawResponse,
    TranscriptResponse ParsedResponse,
    TranscriptTarget Target,
    TranscriptGrade Grade);

public sealed record TranscriptResponse(
    string? Diagnosis,
    string Certainty,
    string Urgency);

public sealed record TranscriptTarget(
    string? Diagnosis,
    string DiagnosticStatus,
    IReadOnlyList<string> AcceptedConcepts,
    IReadOnlyList<string> AcceptedParentConcepts,
    string Urgency);

public sealed record TranscriptGrade(
    string DiagnosisOutcome,
    string? ResolvedConcept,
    bool AcceptedAsParentConcept,
    bool CertaintyCorrect,
    bool UrgencyCorrect,
    bool Undertriage);

/// Aggregate metrics with their exact numerators, denominators, and Wilson intervals.
public sealed record ModelScores(
    string ModelName,
    ReportedRate Coverage,
    ReportedRate SelectiveAccuracy,
    ReportedRate SelectiveRisk,
    ReportedRate DecisionAccuracy,
    ReportedRate AbstentionRecall,
    ReportedRate UnsupportedAnswerRate,
    ReportedRate OverabstentionRate,
    ReportedRate CertaintyAccuracy,
    ReportedRate UrgencyAccuracy,
    ReportedRate UndertriageRate,
    ReportedRate ContrastAccuracy,
    ReportedRate PairedRevisionAccuracy,
    ReportedRate OriginalTargetPersistence,
    ReportedRate ContrastCertaintyAccuracy,
    ReportedRate ContrastUrgencyAccuracy,
    ReportedRate ContrastUndertriageRate,
    int StandardTotal,
    int StandardAnswered,
    int StandardCorrectDiagnoses,
    int StandardWrongDiagnoses,
    int StandardCorrectDeferrals,
    int StandardTargetNull,
    int StandardUnsupportedDiagnoses,
    int StandardTargetNonNull,
    int StandardOverabstentions,
    int StandardCertaintyCorrect,
    int StandardUrgencyCorrect,
    int StandardUndertriage,
    int ContrastTotal,
    int ContrastCorrectDecisions,
    int ContrastOriginalTargetPersistence,
    int ContrastCertaintyCorrect,
    int ContrastUrgencyCorrect,
    int ContrastUndertriage,
    int PairedTotal,
    int PairedRevisionCorrect)
{
    public static ModelScores From(Scorecard scorecard) => new(
        scorecard.ModelName,
        ReportedRate.From(scorecard.Coverage),
        ReportedRate.From(scorecard.SelectiveAccuracy),
        ReportedRate.From(scorecard.SelectiveRisk),
        ReportedRate.From(scorecard.DecisionAccuracy),
        ReportedRate.From(scorecard.AbstentionRecall),
        ReportedRate.From(scorecard.UnsupportedAnswerRate),
        ReportedRate.From(scorecard.OverabstentionRate),
        ReportedRate.From(scorecard.CertaintyAccuracy),
        ReportedRate.From(scorecard.UrgencyAccuracy),
        ReportedRate.From(scorecard.UndertriageRate),
        ReportedRate.From(scorecard.ContrastAccuracy),
        ReportedRate.From(scorecard.PairedRevisionAccuracy),
        ReportedRate.From(scorecard.OriginalTargetPersistence),
        ReportedRate.From(scorecard.ContrastCertaintyAccuracy),
        ReportedRate.From(scorecard.ContrastUrgencyAccuracy),
        ReportedRate.From(scorecard.ContrastUndertriageRate),
        scorecard.StandardTotal,
        scorecard.StandardAnswered,
        scorecard.StandardCorrectDiagnoses,
        scorecard.StandardWrongDiagnoses,
        scorecard.StandardCorrectDeferrals,
        scorecard.StandardTargetNull,
        scorecard.StandardUnsupportedDiagnoses,
        scorecard.StandardTargetNonNull,
        scorecard.StandardOverAbstentions,
        scorecard.StandardCertaintyCorrect,
        scorecard.StandardUrgencyCorrect,
        scorecard.StandardUndertriage,
        scorecard.ContrastTotal,
        scorecard.ContrastCorrectDecisions,
        scorecard.ContrastOriginalTargetPersistence,
        scorecard.ContrastCertaintyCorrect,
        scorecard.ContrastUrgencyCorrect,
        scorecard.ContrastUndertriage,
        scorecard.PairedTotal,
        scorecard.PairedRevisionCorrect);
}

public sealed record ReportedRate(double Value, int Successes, int Total, IReadOnlyList<double> Ci95)
{
    public static ReportedRate From(Rate rate)
        => new(rate.Value, rate.Successes, rate.Total, [rate.Lower, rate.Upper]);
}
