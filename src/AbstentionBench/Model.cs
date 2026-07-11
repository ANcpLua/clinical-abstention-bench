namespace ClinicalAbstentionBench;

/// A single benchmark case: one clinical vignette in two variants.
public sealed record BenchCase(
    string Id,
    string Condition,
    string FullPrompt,
    string AblatedPrompt,
    string ExpectedAnswer,
    string RemovedFact,
    string Rationale);

/// The on-disk shape of data/cases.json.
public sealed record CaseFile(string Note, List<BenchCase> Cases);

public enum Variant { Full, Ablated }

/// One scored unit: a prompt shown to a model plus the answer the evidence supports.
/// A `Full` item is answerable; an `Ablated` item must be abstained on.
public sealed record Item(string CaseId, Variant Variant, string Prompt, string GroundTruth, bool MustAbstain)
{
    public string Key => $"{CaseId}:{VariantName}";

    public string VariantName => Variant == Variant.Full ? "full" : "ablated";

    /// Every case yields exactly two items: the answerable one and the must-abstain one.
    public static IEnumerable<Item> FromCase(BenchCase c)
    {
        yield return new Item(c.Id, Variant.Full, c.FullPrompt, c.ExpectedAnswer, MustAbstain: false);
        yield return new Item(c.Id, Variant.Ablated, c.AblatedPrompt, "INSUFFICIENT", MustAbstain: true);
    }
}

/// A model under test. A real LLM only needs `item.Prompt`; the interface passes the
/// whole item so deterministic fixtures can key off `item.Key`.
public interface IModel
{
    string Name { get; }

    /// Task<string> AnswerAsync is the only thing a model must do.
    Task<string> AnswerAsync(Item item, CancellationToken ct = default);

    /// The system prompt in force, or null for a fixture that never sees one.
    /// Recorded per item in the report so a transcript is self-contained.
    string? SystemPrompt => null;

    /// What was actually run, recorded verbatim in the report's provenance — endpoint, model
    /// digest, sampling settings. Populated after the run, so read it once the model has answered.
    IReadOnlyDictionary<string, string> Provenance => new Dictionary<string, string>();
}

/// Deterministic, credential-free baseline whose reply for each item is read from a fixture.
/// Used for the offline demo and CI so the harness runs with zero API keys. Note that a
/// ScriptedModel is keyed on item id and never sees a system prompt — the baselines are
/// therefore NOT running under the same conditions as a live model.
public sealed class ScriptedModel(string name, IReadOnlyDictionary<string, string> repliesByItemKey) : IModel
{
    public string Name { get; } = name;

    public IReadOnlyDictionary<string, string> Provenance { get; } = new Dictionary<string, string>
    {
        ["kind"] = "scripted-fixture",
        ["source"] = "data/demo-responses.json"
    };

    public Task<string> AnswerAsync(Item item, CancellationToken ct = default)
        => repliesByItemKey.TryGetValue(item.Key, out var reply)
            ? Task.FromResult(reply)
            : throw new KeyNotFoundException($"ScriptedModel '{Name}' has no reply for item '{item.Key}'.");
}
