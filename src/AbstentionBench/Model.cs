using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicalAbstentionBench;

/// A benchmark case has one original diagnostic concept and three evidence states. The target is
/// carried by each state rather than inferred from the arm name: removing a decisive finding can
/// leave a probable working diagnosis, an established broader syndrome, or no supportable diagnosis.
public sealed record BenchCase(
    string Id,
    CaseVariants Variants,
    string Adjudication)
{
    public CaseVariant Full => Variants.Full;

    public CaseVariant Ablated => Variants.Ablated;

    public CaseVariant Contrast => Variants.Contrast;

    /// The full variant owns the original concept; storing it a second time would allow drift.
    public string OriginalConcept
        => Full.Target.Diagnosis
           ?? throw new InvalidDataException($"Case '{Id}' has no diagnosis on its full variant.");

    public string Condition => OriginalConcept.Replace('_', ' ');
}

/// The on-disk shape of data/cases.json.
public sealed record CaseFile(List<BenchCase> Cases);

/// The three evidence states are nested in JSON so case-level adjudication is not repeated in each
/// item. Their relation to the original concept is derived from the property name.
public sealed record CaseVariants(CaseVariant Full, CaseVariant Ablated, CaseVariant Contrast);

/// One evidence state of a case.
public sealed record CaseVariant(
    string Vignette,
    Target Target);

/// How a variant relates to the concept around which the case was authored. These labels describe
/// evidence construction; they do not determine whether the target diagnosis is null.
[JsonConverter(typeof(EvidenceRelationJsonConverter))]
public enum EvidenceRelation
{
    OriginalSupported,
    EvidenceAblated,
    AlternativeSupported
}

/// The diagnostic claim licensed by the evidence, independent of clinical urgency.
[JsonConverter(typeof(DiagnosticStatusJsonConverter))]
public enum DiagnosticStatus
{
    Established,
    Probable,
    Indeterminate
}

/// The urgency of the next clinical action. Diagnostic deferral is never treated as permission to
/// defer care: an indeterminate diagnosis can still be an emergency.
[JsonConverter(typeof(UrgencyJsonConverter))]
public enum Urgency
{
    Emergency,
    Urgent,
    Routine
}

/// The answer supported by one variant.
///
/// `Diagnosis` is a canonical concept id, or null when no diagnosis is supportable. Additional
/// same-level concepts and deliberately accepted broader concepts are explicit; the grader never
/// infers them from substrings or an ontology hierarchy.
public sealed record Target(
    string? Diagnosis,
    DiagnosticStatus DiagnosticStatus,
    IReadOnlyList<string> AcceptedConcepts,
    IReadOnlyList<string>? AcceptedParentConcepts,
    Urgency Urgency)
{
    public bool HasDiagnosis => Diagnosis is not null;

    public IReadOnlyList<string> AllAcceptedConcepts
        => Diagnosis is null
            ? AcceptedConcepts
            : [Diagnosis, .. AcceptedConcepts.Where(c => !string.Equals(c, Diagnosis, StringComparison.Ordinal))];
}

/// A diagnostic concept and every whole-field surface form that resolves to it. Aliases are data,
/// not grader heuristics. Their complete fields are matched case-insensitively after outer whitespace
/// is trimmed; embedded punctuation, wording, and token order remain significant.
public sealed record DiagnosticConcept(
    string Id,
    string PreferredName,
    IReadOnlyList<string> Aliases);

/// The on-disk shape of data/concepts.json.
public sealed record ConceptFile(List<DiagnosticConcept> Concepts);

/// Result of resolving one structured `diagnosis` field through the concept catalog.
public sealed record ConceptResolution(string ConceptId, string MatchedForm);

/// Whole-field concept resolver. Construction fails on duplicate ids or aliases that would make a
/// response ambiguous; grading therefore cannot silently depend on declaration order.
public sealed class ConceptCatalog
{
    private readonly IReadOnlyDictionary<string, ConceptResolution> _forms;
    private readonly IReadOnlyDictionary<string, DiagnosticConcept> _concepts;

    public ConceptCatalog(IEnumerable<DiagnosticConcept> concepts)
    {
        var byId = new Dictionary<string, DiagnosticConcept>(StringComparer.Ordinal);
        var forms = new Dictionary<string, ConceptResolution>(StringComparer.OrdinalIgnoreCase);

        foreach (var concept in concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Id))
                throw new InvalidDataException("A diagnostic concept id cannot be empty.");
            if (string.IsNullOrWhiteSpace(concept.PreferredName))
                throw new InvalidDataException($"Concept '{concept.Id}' has an empty preferred name.");
            if (!byId.TryAdd(concept.Id, concept))
                throw new InvalidDataException($"Duplicate diagnostic concept id '{concept.Id}'.");

            AddForm(concept.Id, concept.Id, concept.Id, forms);
            AddForm(concept.PreferredName, concept.Id, concept.PreferredName, forms);
            foreach (var alias in concept.Aliases)
                AddForm(alias, concept.Id, alias, forms);
        }

        if (byId.Count == 0)
            throw new InvalidDataException("The concept catalog contains zero concepts.");

        _concepts = byId;
        _forms = forms;
    }

    public IReadOnlyCollection<DiagnosticConcept> Concepts => [.. _concepts.Values];

    public bool Contains(string conceptId) => _concepts.ContainsKey(conceptId);

    public ConceptResolution? Resolve(string? diagnosis)
    {
        if (string.IsNullOrWhiteSpace(diagnosis))
            return null;
        return _forms.TryGetValue(diagnosis.Trim(), out var resolution) ? resolution : null;
    }

    private static void AddForm(
        string form,
        string conceptId,
        string declaredForm,
        IDictionary<string, ConceptResolution> forms)
    {
        if (string.IsNullOrWhiteSpace(form))
            throw new InvalidDataException($"Concept '{conceptId}' contains an empty alias.");

        var normalized = form.Trim();
        if (forms.TryGetValue(normalized, out var existing))
        {
            if (!string.Equals(existing.ConceptId, conceptId, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Diagnostic form '{normalized}' is ambiguous between concepts " +
                    $"'{existing.ConceptId}' and '{conceptId}'.");
            return;
        }

        forms.Add(normalized, new ConceptResolution(conceptId, declaredForm));
    }
}

/// The three case arms. `Contrast` supports an explicit alternative target instead of assuming that
/// every changed vignette should be unanswered.
[JsonConverter(typeof(VariantJsonConverter))]
public enum Variant
{
    Full,
    Ablated,
    Contrast
}

/// One model-facing benchmark item. It contains evidence and targets, but no pre-rendered instruction;
/// the runner records the actual rendered prompt separately in ItemResult.
public sealed record Item(
    string CaseId,
    Variant Variant,
    string OriginalConcept,
    CaseVariant CaseVariant)
{
    public string Key => $"{CaseId}:{VariantName}";

    public string VariantName => Variant.WireName();

    public string Vignette => CaseVariant.Vignette;

    public EvidenceRelation Relation => Variant switch
    {
        Variant.Full => EvidenceRelation.OriginalSupported,
        Variant.Ablated => EvidenceRelation.EvidenceAblated,
        Variant.Contrast => EvidenceRelation.AlternativeSupported,
        _ => throw new ArgumentOutOfRangeException(nameof(Variant))
    };

    public Target Target => CaseVariant.Target;

    public static IEnumerable<Item> FromCase(BenchCase c)
    {
        yield return new Item(c.Id, Variant.Full, c.OriginalConcept, c.Full);
        yield return new Item(c.Id, Variant.Ablated, c.OriginalConcept, c.Ablated);
        yield return new Item(c.Id, Variant.Contrast, c.OriginalConcept, c.Contrast);
    }
}

/// The only item data a live model receives. `Prompt` is the complete user message actually sent,
/// not merely the vignette, so it can be retained verbatim in the transcript.
public sealed record ModelInput(string ItemKey, string Prompt);

public interface IModel
{
    string Name { get; }

    Task<string> AnswerAsync(ModelInput input, CancellationToken ct = default);

    string? SystemPrompt => null;

    bool IsBaseline => false;

    IReadOnlyDictionary<string, string> Provenance => new Dictionary<string, string>();
}

public enum ReferencePolicy
{
    AlwaysAnswer,
    AlwaysAbstain,
    LabelOracle
}

/// Deterministic analytical policies. Replies use the same strict JSON contract as live models.
public sealed class ReferencePolicyModel : IModel
{
    private readonly IReadOnlyDictionary<string, string> _responses;

    public ReferencePolicyModel(ReferencePolicy policy, IEnumerable<BenchCase> cases)
    {
        Policy = policy;
        Name = policy switch
        {
            ReferencePolicy.AlwaysAnswer => "AlwaysAnswerBaseline",
            ReferencePolicy.AlwaysAbstain => "AlwaysAbstainBaseline",
            ReferencePolicy.LabelOracle => "LabelOracleBaseline",
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };

        _responses = cases
            .SelectMany(c => Item.FromCase(c).Select(item => new
            {
                item.Key,
                Response = StructuredResponseJson.Serialize(policy switch
                {
                    ReferencePolicy.AlwaysAnswer => new ModelResponse(
                        c.OriginalConcept, DiagnosticStatus.Established, Urgency.Routine),
                    ReferencePolicy.AlwaysAbstain => new ModelResponse(
                        null, DiagnosticStatus.Indeterminate, Urgency.Routine),
                    ReferencePolicy.LabelOracle => new ModelResponse(
                        item.Target.Diagnosis, item.Target.DiagnosticStatus, item.Target.Urgency),
                    _ => throw new ArgumentOutOfRangeException(nameof(policy))
                })
            }))
            .ToDictionary(x => x.Key, x => x.Response, StringComparer.Ordinal);

        Provenance = new Dictionary<string, string>
        {
            ["kind"] = "deterministic-reference-policy",
            ["policy"] = policy.ToString(),
            ["source"] = "programmatic policy over benchmark targets",
            ["labelAccess"] = policy switch
            {
                ReferencePolicy.AlwaysAnswer => "originalConcept",
                ReferencePolicy.AlwaysAbstain => "none",
                ReferencePolicy.LabelOracle => "target diagnosis, diagnostic status, and urgency",
                _ => throw new ArgumentOutOfRangeException(nameof(policy))
            },
            ["systemPrompt"] = "none — reference policies never see one"
        };
    }

    public ReferencePolicy Policy { get; }

    public string Name { get; }

    public bool IsBaseline => true;

    public IReadOnlyDictionary<string, string> Provenance { get; }

    public Task<string> AnswerAsync(ModelInput input, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _responses.TryGetValue(input.ItemKey, out var response)
            ? Task.FromResult(response)
            : throw new KeyNotFoundException($"Reference policy '{Name}' has no item '{input.ItemKey}'.");
    }
}

/// Strict structured reply from every model. `Diagnosis` is a complete surface form or null; the
/// grader resolves it through ConceptCatalog without substring matching.
public sealed record ModelResponse(string? Diagnosis, DiagnosticStatus Certainty, Urgency Urgency);

public static class StructuredResponseJson
{
    public static string Serialize(ModelResponse response) => JsonSerializer.Serialize(new
    {
        diagnosis = response.Diagnosis,
        certainty = response.Certainty.WireName(),
        urgency = response.Urgency.WireName()
    });
}

public static class ClinicalEnumNames
{
    public static string WireName(this Variant value) => value switch
    {
        Variant.Full => "full",
        Variant.Ablated => "ablated",
        Variant.Contrast => "contrast",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string WireName(this EvidenceRelation value) => value switch
    {
        EvidenceRelation.OriginalSupported => "original-supported",
        EvidenceRelation.EvidenceAblated => "evidence-ablated",
        EvidenceRelation.AlternativeSupported => "alternative-supported",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string WireName(this DiagnosticStatus value) => value switch
    {
        DiagnosticStatus.Established => "established",
        DiagnosticStatus.Probable => "probable",
        DiagnosticStatus.Indeterminate => "indeterminate",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string WireName(this Urgency value) => value switch
    {
        Urgency.Emergency => "emergency",
        Urgency.Urgent => "urgent",
        Urgency.Routine => "routine",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static EvidenceRelation ParseEvidenceRelation(string value) => value switch
    {
        "original-supported" => EvidenceRelation.OriginalSupported,
        "evidence-ablated" => EvidenceRelation.EvidenceAblated,
        "alternative-supported" => EvidenceRelation.AlternativeSupported,
        _ => throw new JsonException($"Unknown evidence relation '{value}'.")
    };

    public static DiagnosticStatus ParseDiagnosticStatus(string value) => value switch
    {
        "established" => DiagnosticStatus.Established,
        "probable" => DiagnosticStatus.Probable,
        "indeterminate" => DiagnosticStatus.Indeterminate,
        _ => throw new JsonException($"Unknown diagnostic status '{value}'.")
    };

    public static Urgency ParseUrgency(string value) => value switch
    {
        "emergency" => Urgency.Emergency,
        "urgent" => Urgency.Urgent,
        "routine" => Urgency.Routine,
        _ => throw new JsonException($"Unknown urgency '{value}'.")
    };
}

public sealed class EvidenceRelationJsonConverter : JsonConverter<EvidenceRelation>
{
    public override EvidenceRelation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ClinicalEnumNames.ParseEvidenceRelation(reader.GetString() ?? throw new JsonException("Evidence relation must be a string."));

    public override void Write(Utf8JsonWriter writer, EvidenceRelation value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.WireName());
}

public sealed class DiagnosticStatusJsonConverter : JsonConverter<DiagnosticStatus>
{
    public override DiagnosticStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ClinicalEnumNames.ParseDiagnosticStatus(reader.GetString() ?? throw new JsonException("Diagnostic status must be a string."));

    public override void Write(Utf8JsonWriter writer, DiagnosticStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.WireName());
}

public sealed class UrgencyJsonConverter : JsonConverter<Urgency>
{
    public override Urgency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ClinicalEnumNames.ParseUrgency(reader.GetString() ?? throw new JsonException("Urgency must be a string."));

    public override void Write(Utf8JsonWriter writer, Urgency value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.WireName());
}

public sealed class VariantJsonConverter : JsonConverter<Variant>
{
    public override Variant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (reader.GetString() ?? throw new JsonException("Variant must be a string.")) switch
        {
            "full" => Variant.Full,
            "ablated" => Variant.Ablated,
            "contrast" => Variant.Contrast,
            var value => throw new JsonException($"Unknown variant '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, Variant value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.WireName());
}
