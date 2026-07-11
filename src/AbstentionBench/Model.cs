namespace ClinicalAbstentionBench;

/// A single benchmark case: one clinical vignette in three variants.
///
/// `AcceptedAnswers` are the other surface forms that name the same diagnosis — abbreviations,
/// spelling variants, and the more- or less-specific names a clinician would also accept. They exist
/// because the grader matches words, not meanings: without them, a model that answered "STEMI" or
/// "Iron deficiency anemia" scored WRONG against "ST-elevation myocardial infarction" and
/// "Iron-deficiency anemia" — a fact about the grader, not about the model.
public sealed record BenchCase(
    string Id,
    string Condition,
    string FullPrompt,
    string AblatedPrompt,
    string CounterfactualPrompt,
    string ExpectedAnswer,
    string RemovedFact,
    string Rationale,
    IReadOnlyList<string>? AcceptedAnswers = null,
    string? FlippedFact = null,
    string? CounterfactualRationale = null,
    /// Other surface forms, besides ExpectedAnswer, that the flipped finding actually excludes.
    /// Null means every AcceptedAnswer is equivalent here. A narrower explicit list is needed when a
    /// full-item alias is broader than the finding being flipped (for example, MI versus STEMI).
    IReadOnlyList<string>? CounterfactualExcludedAnswers = null);

/// The on-disk shape of data/cases.json.
public sealed record CaseFile(string Note, List<BenchCase> Cases);

/// The three ways one vignette is shown to a model.
///
/// `Full` and `Ablated` ask "does it answer when it can, and decline when it cannot?".
/// `Counterfactual` asks a different and harder question: **did it read the decisive finding at all?**
/// A model that ignores the labs and pattern-matches the shape of the vignette scores exactly like a
/// model that reads them and is merely overconfident — 100 % answer accuracy, 100 % unsupported
/// answers. That is llama3.2:3b's scorecard, and it is also AlwaysAnswerBaseline's. Those are
/// different failure modes with different remedies, and only the counterfactual arm separates them.
public enum Variant { Full, Ablated, Counterfactual }

/// One scored unit: a prompt shown to a model plus the answer the evidence supports.
///  - `Full`: answerable.
///  - `Ablated`: the decisive finding is gone, so it must be abstained on.
///  - `Counterfactual`: the decisive finding is FLIPPED and now excludes the original diagnosis, so it
///    must also be abstained on — and `ExcludedAnswers` names what the model must NOT say.
public sealed record Item(
    string CaseId,
    Variant Variant,
    string Prompt,
    string GroundTruth,
    bool MustAbstain,
    IReadOnlyList<string>? AcceptedAnswers = null,
    IReadOnlyList<string>? ExcludedAnswers = null)
{
    public string Key => $"{CaseId}:{VariantName}";

    public string VariantName => Variant switch
    {
        Variant.Full => "full",
        Variant.Ablated => "ablated",
        Variant.Counterfactual => "counterfactual",
        _ => throw new ArgumentOutOfRangeException(nameof(Variant))
    };

    /// Every surface form that counts as naming the supported answer: the canonical one plus the
    /// case's accepted synonyms.
    public IReadOnlyList<string> AcceptedForms => [GroundTruth, .. AcceptedAnswers ?? []];

    /// Surface forms the evidence now rules OUT. Saying one of these on a counterfactual item means
    /// the model cannot have read the finding — the finding says no.
    public IReadOnlyList<string> ExcludedForms => ExcludedAnswers ?? [];

    /// Every case yields three items: the answerable one, and the two that must be declined — one
    /// because the evidence is absent, one because the evidence actively contradicts.
    public static IEnumerable<Item> FromCase(BenchCase c)
    {
        IReadOnlyList<string> excludedDiagnosis =
        [
            c.ExpectedAnswer,
            .. c.CounterfactualExcludedAnswers ?? c.AcceptedAnswers ?? []
        ];

        yield return new Item(c.Id, Variant.Full, c.FullPrompt, c.ExpectedAnswer, MustAbstain: false, c.AcceptedAnswers);
        yield return new Item(c.Id, Variant.Ablated, c.AblatedPrompt, "INSUFFICIENT", MustAbstain: true);
        yield return new Item(c.Id, Variant.Counterfactual, c.CounterfactualPrompt, "INSUFFICIENT", MustAbstain: true,
            AcceptedAnswers: null, ExcludedAnswers: excludedDiagnosis);
    }
}

/// One named system prompt, loaded from data/prompts.json.
///
/// The system prompt is a **controlled variable**, not a constant. Abstention is strongly
/// prompt-sensitive: an unsupported-answer rate measured under one instruction is a claim about that
/// prompt-and-model pair, and saying "llama3.2:3b answers when it shouldn't" without naming the
/// prompt is saying less than it appears to.
public sealed record SystemPrompt(string Name, string Text, string Description);

/// The on-disk shape of data/prompts.json.
public sealed record PromptFile(string Default, List<SystemPrompt> Prompts);

/// The only item data a model receives. Ground-truth labels and scoring metadata are deliberately
/// absent: an adapter cannot accidentally leak the answer into the inference request.
public sealed record ModelInput(string ItemKey, string Prompt);

/// A model under test. Live adapters receive only ModelInput, while deterministic reference policies
/// keep any label access inside their own explicitly reported construction.
public interface IModel
{
    string Name { get; }

    /// Task<string> AnswerAsync is the only thing a model must do.
    Task<string> AnswerAsync(ModelInput input, CancellationToken ct = default);

    /// The system prompt in force, or null for a reference policy that never sees one.
    /// Recorded per item in the report so a transcript is self-contained.
    string? SystemPrompt => null;

    /// True for a deterministic reference policy. Reported separately from live models because a
    /// programmatic policy is an analytical reference point, not a competitor.
    bool IsBaseline => false;

    /// What was actually run, recorded verbatim in the report's provenance — endpoint, model
    /// digest, sampling settings. Populated after the run, so read it once the model has answered.
    IReadOnlyDictionary<string, string> Provenance => new Dictionary<string, string>();
}

/// The three analytical policies shown beside live results. None is learned and none reads a system
/// prompt; their behavior and any label access are declared in report provenance.
public enum ReferencePolicy
{
    AlwaysAnswer,
    AlwaysAbstain,
    LabelOracle
}

/// Deterministic, credential-free reference policy generated from data/cases.json.
///
/// LabelOracle is perfect by construction: it answers Full items and declines the other variants by
/// consulting their labels. It demonstrates the metric's target, not the clinical validity of those
/// labels. AlwaysAnswer and AlwaysAbstain are the two degenerate poles.
public sealed class ReferencePolicyModel : IModel
{
    public const string AbstentionResponse = "INSUFFICIENT INFORMATION.";

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
                Response = policy switch
                {
                    ReferencePolicy.AlwaysAnswer => c.ExpectedAnswer + ".",
                    ReferencePolicy.AlwaysAbstain => AbstentionResponse,
                    ReferencePolicy.LabelOracle when item.Variant == Variant.Full => c.ExpectedAnswer + ".",
                    ReferencePolicy.LabelOracle => AbstentionResponse,
                    _ => throw new ArgumentOutOfRangeException(nameof(policy))
                }
            }))
            .ToDictionary(x => x.Key, x => x.Response, StringComparer.Ordinal);

        Provenance = new Dictionary<string, string>
        {
            ["kind"] = "deterministic-reference-policy",
            ["policy"] = policy.ToString(),
            ["source"] = "programmatic policy over data/cases.json items",
            ["labelAccess"] = policy switch
            {
                ReferencePolicy.AlwaysAnswer => "expectedAnswer",
                ReferencePolicy.AlwaysAbstain => "none",
                ReferencePolicy.LabelOracle => "expectedAnswer and variant",
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
