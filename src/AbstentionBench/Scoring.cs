namespace ClinicalAbstentionBench;

/// Diagnostic decision on one item. Certainty and urgency are deliberately not folded into this
/// enum; each is scored independently in ItemGrade.
public enum DiagnosisOutcome
{
    CorrectDiagnosis,
    WrongDiagnosis,
    CorrectDeferral,
    UnsupportedDiagnosis,
    OverAbstention
}

public static class DiagnosisOutcomeNames
{
    public static string WireName(this DiagnosisOutcome outcome) => outcome switch
    {
        DiagnosisOutcome.CorrectDiagnosis => "correct-diagnosis",
        DiagnosisOutcome.WrongDiagnosis => "wrong-diagnosis",
        DiagnosisOutcome.CorrectDeferral => "correct-deferral",
        DiagnosisOutcome.UnsupportedDiagnosis => "unsupported-diagnosis",
        DiagnosisOutcome.OverAbstention => "overabstention",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };
}

/// One model, one item, one raw reply, and its independent dimension grades. `SentPrompt` is the
/// complete user message passed to the adapter; it is not reconstructed from the item at report time.
public sealed record ItemResult(
    string ModelName,
    Item Item,
    string? SystemPrompt,
    string SentPrompt,
    string RawResponse,
    ItemGrade Grade);

/// Aggregate results for one model/prompt profile.
///
/// Primary selective-prediction metrics use only Full + Ablated. Contrast is a paired revision task
/// with its own metrics, so adding that arm cannot change the class balance of the primary score.
public sealed record Scorecard(
    string ModelName,
    int StandardTotal,
    int StandardAnswered,
    int StandardCorrectDiagnoses,
    int StandardWrongDiagnoses,
    int StandardCorrectDeferrals,
    int StandardTargetNull,
    int StandardUnsupportedDiagnoses,
    int StandardTargetNonNull,
    int StandardOverAbstentions,
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
    /// Fraction of primary items on which the model supplied a diagnosis.
    public Rate Coverage => new(StandardAnswered, StandardTotal);

    /// Diagnostic accuracy conditional on the model answering. Unsupported diagnoses on null-target
    /// items are answered and incorrect, so they remain in this denominator.
    public Rate SelectiveAccuracy => new(StandardCorrectDiagnoses, StandardAnswered);

    /// The complement of selective accuracy: wrong or unsupported diagnoses among answered items.
    public Rate SelectiveRisk
        => new(StandardWrongDiagnoses + StandardUnsupportedDiagnoses, StandardAnswered);

    /// Accuracy of the answer/defer decision, crediting both a correct diagnosis and a correct
    /// diagnostic deferral.
    public Rate DecisionAccuracy
        => new(StandardCorrectDiagnoses + StandardCorrectDeferrals, StandardTotal);

    /// Recall of abstention only where the diagnostic target is actually null.
    public Rate AbstentionRecall => new(StandardCorrectDeferrals, StandardTargetNull);

    /// Diagnoses supplied where the diagnostic target is null.
    public Rate UnsupportedAnswerRate => new(StandardUnsupportedDiagnoses, StandardTargetNull);

    /// Diagnostic deferrals where a non-null diagnosis is supported.
    public Rate OverabstentionRate => new(StandardOverAbstentions, StandardTargetNonNull);

    public Rate CertaintyAccuracy => new(StandardCertaintyCorrect, StandardTotal);

    public Rate UrgencyAccuracy => new(StandardUrgencyCorrect, StandardTotal);

    public Rate UndertriageRate => new(StandardUndertriage, StandardTotal);

    /// Correct diagnostic decisions in the alternative-supported arm.
    public Rate ContrastAccuracy => new(ContrastCorrectDecisions, ContrastTotal);

    /// Contrast cases on which the response still resolves to a concept accepted on the full state
    /// but not on the contrast state, including original-only parent concepts.
    public Rate OriginalTargetPersistence
        => new(ContrastOriginalTargetPersistence, ContrastTotal);

    /// Cases where both diagnostic decisions were correct and the contrast response resolved to a
    /// concept supported by the contrast but not by the full state.
    public Rate PairedRevisionAccuracy => new(PairedRevisionCorrect, PairedTotal);

    public Rate ContrastCertaintyAccuracy => new(ContrastCertaintyCorrect, ContrastTotal);

    public Rate ContrastUrgencyAccuracy => new(ContrastUrgencyCorrect, ContrastTotal);

    public Rate ContrastUndertriageRate => new(ContrastUndertriage, ContrastTotal);

    public static Scorecard From(string model, IEnumerable<ItemResult> source)
    {
        var results = source.ToList();
        if (results.Any(result => !string.Equals(result.ModelName, model, StringComparison.Ordinal)))
            throw new InvalidDataException(
                $"Scorecard '{model}' received results belonging to a different model.");

        var duplicate = results
            .GroupBy(result => result.Item.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException(
                $"Scorecard '{model}' received duplicate result '{duplicate.Key}'.");

        var standard = results.Where(result => result.Item.Variant is Variant.Full or Variant.Ablated).ToList();
        var contrast = results.Where(result => result.Item.Variant == Variant.Contrast).ToList();

        var standardAnswered = standard.Count(result => result.Grade.Answered);
        var standardCorrectDiagnoses = standard.Count(result =>
            result.Grade.DiagnosisOutcome == DiagnosisOutcome.CorrectDiagnosis);
        var standardWrongDiagnoses = standard.Count(result =>
            result.Grade.DiagnosisOutcome == DiagnosisOutcome.WrongDiagnosis);
        var standardCorrectDeferrals = standard.Count(result =>
            result.Grade.DiagnosisOutcome == DiagnosisOutcome.CorrectDeferral);
        var standardTargetNull = standard.Count(result => !result.Item.Target.HasDiagnosis);
        var standardUnsupported = standard.Count(result =>
            result.Grade.DiagnosisOutcome == DiagnosisOutcome.UnsupportedDiagnosis);
        var standardTargetNonNull = standard.Count(result => result.Item.Target.HasDiagnosis);
        var standardOverabstentions = standard.Count(result =>
            result.Grade.DiagnosisOutcome == DiagnosisOutcome.OverAbstention);

        var contrastCorrect = contrast.Count(result => result.Grade.DiagnosisDecisionCorrect);

        var byCase = results
            .GroupBy(result => result.Item.CaseId, StringComparer.Ordinal)
            .Select(group => new
            {
                Full = group.SingleOrDefault(result => result.Item.Variant == Variant.Full),
                Contrast = group.SingleOrDefault(result => result.Item.Variant == Variant.Contrast)
            })
            .Where(pair => pair.Full is not null && pair.Contrast is not null)
            .ToList();

        // Persistence is concept-aware, not exact-id-only. A response that backs off from
        // "inferior STEMI" to the still-original parent "STEMI" has not revised to the contrast.
        // A parent shared by both targets (for example acute MI for STEMI -> NSTEMI) is not counted.
        var persistence = byCase.Count(pair =>
            pair.Contrast!.Grade.ResolvedConcept is { } concept
            && Accepts(pair.Full!.Item.Target, concept)
            && !Accepts(pair.Contrast.Item.Target, concept));

        var pairedCorrect = byCase.Count(pair =>
            pair.Full!.Grade.DiagnosisOutcome == DiagnosisOutcome.CorrectDiagnosis
            && pair.Contrast!.Grade.DiagnosisOutcome == DiagnosisOutcome.CorrectDiagnosis
            && pair.Contrast.Grade.ResolvedConcept is { } contrastConcept
            && !Accepts(pair.Full.Item.Target, contrastConcept));

        return new Scorecard(
            model,
            standard.Count,
            standardAnswered,
            standardCorrectDiagnoses,
            standardWrongDiagnoses,
            standardCorrectDeferrals,
            standardTargetNull,
            standardUnsupported,
            standardTargetNonNull,
            standardOverabstentions,
            standard.Count(result => result.Grade.CertaintyCorrect),
            standard.Count(result => result.Grade.UrgencyCorrect),
            standard.Count(result => result.Grade.Undertriage),
            contrast.Count,
            contrastCorrect,
            persistence,
            contrast.Count(result => result.Grade.CertaintyCorrect),
            contrast.Count(result => result.Grade.UrgencyCorrect),
            contrast.Count(result => result.Grade.Undertriage),
            byCase.Count,
            pairedCorrect);
    }

    private static bool Accepts(Target target, string concept)
        => target.AllAcceptedConcepts.Contains(concept, StringComparer.Ordinal)
           || (target.AcceptedParentConcepts ?? []).Contains(concept, StringComparer.Ordinal);
}
