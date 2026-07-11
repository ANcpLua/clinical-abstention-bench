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
    public string Key => $"{CaseId}:{(Variant == Variant.Full ? "full" : "ablated")}";

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
    Task<string> AnswerAsync(Item item, CancellationToken ct = default);
}

/// Deterministic, credential-free baseline whose reply for each item is read from a fixture.
/// Used for the offline demo and CI so the harness runs with zero API keys. Note that a
/// ScriptedModel is keyed on item id and never sees a system prompt — the baselines are
/// therefore NOT running under the same conditions as a live model.
public sealed class ScriptedModel(string name, IReadOnlyDictionary<string, string> repliesByItemKey) : IModel
{
    public string Name { get; } = name;

    public Task<string> AnswerAsync(Item item, CancellationToken ct = default)
        => repliesByItemKey.TryGetValue(item.Key, out var reply)
            ? Task.FromResult(reply)
            : throw new KeyNotFoundException($"ScriptedModel '{Name}' has no reply for item '{item.Key}'.");
}
