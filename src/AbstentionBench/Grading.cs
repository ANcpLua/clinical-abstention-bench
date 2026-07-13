using System.Text.Json;

namespace ClinicalAbstentionBench;

/// Turns one strict structured reply into independently auditable diagnostic, certainty, and urgency
/// judgements. Parsing errors are run errors: malformed output is never silently relabelled as a
/// clinical abstention.
public interface IGrader
{
    string Name { get; }

    ItemGrade Score(Item item, string rawResponse);
}

/// The complete grade for one item. `ResolvedConcept` is null both for a deliberate diagnostic
/// deferral and for an unknown surface form; DiagnosisOutcome distinguishes those cases.
public sealed record ItemGrade(
    ModelResponse Response,
    string? ResolvedConcept,
    DiagnosisOutcome DiagnosisOutcome,
    bool AcceptedAsParentConcept,
    bool CertaintyCorrect,
    bool UrgencyCorrect,
    bool Undertriage)
{
    public bool Answered => Response.Diagnosis is not null;

    public bool DiagnosisDecisionCorrect
        => DiagnosisOutcome is DiagnosisOutcome.CorrectDiagnosis or DiagnosisOutcome.CorrectDeferral;
}

/// Exact whole-field grader over the external concept catalog.
///
/// A diagnosis resolves only when the complete structured `diagnosis` value equals a concept id,
/// preferred name, or declared alias (case-insensitively, with outer whitespace ignored). There are
/// no substring, token, negation, morphology, or ontology inferences. A broader diagnosis is accepted
/// only when its concept id appears in the target's AcceptedParentConcepts.
public sealed class StructuredConceptGrader : IGrader
{
    private readonly ConceptCatalog _concepts;

    public StructuredConceptGrader(ConceptCatalog concepts)
    {
        _concepts = concepts;
    }

    public string Name => "structured-concept";

    public ItemGrade Score(Item item, string rawResponse)
    {
        ValidateTarget(item);

        var response = ParseResponse(rawResponse, item.Key);
        var resolution = _concepts.Resolve(response.Diagnosis);
        var acceptedAsParent = resolution is not null
                               && (item.Target.AcceptedParentConcepts ?? []).Contains(
                                   resolution.ConceptId, StringComparer.Ordinal);

        var diagnosisOutcome = Diagnosis(item.Target, response, resolution, acceptedAsParent);
        var urgencyCorrect = response.Urgency == item.Target.Urgency;

        return new ItemGrade(
            response,
            resolution?.ConceptId,
            diagnosisOutcome,
            acceptedAsParent,
            response.Certainty == item.Target.DiagnosticStatus,
            urgencyCorrect,
            !urgencyCorrect && UrgencyRank(response.Urgency) < UrgencyRank(item.Target.Urgency));
    }

    private static DiagnosisOutcome Diagnosis(
        Target target,
        ModelResponse response,
        ConceptResolution? resolution,
        bool acceptedAsParent)
    {
        if (!target.HasDiagnosis)
            return response.Diagnosis is null
                ? DiagnosisOutcome.CorrectDeferral
                : DiagnosisOutcome.UnsupportedDiagnosis;

        if (response.Diagnosis is null)
            return DiagnosisOutcome.OverAbstention;

        if (resolution is null)
            return DiagnosisOutcome.WrongDiagnosis;

        var accepted = target.AllAcceptedConcepts.Contains(resolution.ConceptId, StringComparer.Ordinal);
        return accepted || acceptedAsParent
            ? DiagnosisOutcome.CorrectDiagnosis
            : DiagnosisOutcome.WrongDiagnosis;
    }

    private void ValidateTarget(Item item)
    {
        var target = item.Target;

        if (!_concepts.Contains(item.OriginalConcept))
            throw new InvalidDataException(
                $"Item '{item.Key}' refers to unknown original concept '{item.OriginalConcept}'.");

        if (!target.HasDiagnosis)
        {
            if (target.AcceptedConcepts.Count != 0 || (target.AcceptedParentConcepts?.Count ?? 0) != 0)
                throw new InvalidDataException(
                    $"Item '{item.Key}' has a null target diagnosis but non-empty accepted concept lists.");
            if (target.DiagnosticStatus != DiagnosticStatus.Indeterminate)
                throw new InvalidDataException(
                    $"Item '{item.Key}' has a null target diagnosis but status '{target.DiagnosticStatus.WireName()}'.");
            return;
        }

        if (target.DiagnosticStatus == DiagnosticStatus.Indeterminate)
            throw new InvalidDataException(
                $"Item '{item.Key}' has target diagnosis '{target.Diagnosis}' but indeterminate status.");

        foreach (var concept in target.AllAcceptedConcepts.Concat(target.AcceptedParentConcepts ?? []))
        {
            if (!_concepts.Contains(concept))
                throw new InvalidDataException(
                    $"Item '{item.Key}' refers to unknown target concept '{concept}'.");
        }

    }

    /// Parse exactly one JSON object with exactly the three public response fields. Markdown fences,
    /// prose, duplicate members, additional metadata, unknown enum values, and blank diagnoses fail.
    public static ModelResponse ParseResponse(string rawResponse, string itemKey = "unknown")
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            throw new InvalidDataException($"Model returned an empty response for item '{itemKey}'.");

        try
        {
            using var document = JsonDocument.Parse(rawResponse, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("The response root must be an object.");

            var members = root.EnumerateObject().ToList();
            if (members.Count != 3
                || members.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count() != 3
                || members.Any(member => member.Name is not ("diagnosis" or "certainty" or "urgency")))
            {
                throw new JsonException(
                    "The response must contain exactly diagnosis, certainty, and urgency once each.");
            }

            var diagnosisElement = root.GetProperty("diagnosis");
            string? diagnosis = diagnosisElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => diagnosisElement.GetString(),
                _ => throw new JsonException("diagnosis must be a string or null.")
            };
            if (diagnosis is not null && string.IsNullOrWhiteSpace(diagnosis))
                throw new JsonException("diagnosis cannot be an empty string; use null to defer.");

            var certaintyElement = root.GetProperty("certainty");
            if (certaintyElement.ValueKind != JsonValueKind.String)
                throw new JsonException("certainty must be a string.");

            var urgencyElement = root.GetProperty("urgency");
            if (urgencyElement.ValueKind != JsonValueKind.String)
                throw new JsonException("urgency must be a string.");

            return new ModelResponse(
                diagnosis?.Trim(),
                ClinicalEnumNames.ParseDiagnosticStatus(certaintyElement.GetString()!),
                ClinicalEnumNames.ParseUrgency(urgencyElement.GetString()!));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Model response for item '{itemKey}' violates the structured response contract: {ex.Message}", ex);
        }
    }

    private static int UrgencyRank(Urgency urgency) => urgency switch
    {
        Urgency.Routine => 0,
        Urgency.Urgent => 1,
        Urgency.Emergency => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(urgency))
    };
}
