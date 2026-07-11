namespace ClinicalAbstentionBench;

/// The outcome of a CI gate check: did every gated model clear the abstention-recall threshold?
public sealed record GateResult(bool Passed, double Threshold, IReadOnlyList<string> FailingModels);

/// The `--gate <recall>` check, kept out of the CLI so it is unit-testable.
///
/// The gate is applied to exactly the models that were run. Model selection (`--only`,
/// `--no-baselines`) is therefore also gate selection: pointing the gate at a real model in CI
/// means running only that model. Gating an unfiltered run would be meaningless, because
/// AlwaysAnswerBaseline has 0 % abstention recall by construction and would fail every threshold.
public static class Gate
{
    public static GateResult Check(IEnumerable<Scorecard> cards, double threshold)
    {
        var failing = cards
            .Where(c => c.AbstentionRecall < threshold)
            .Select(c => c.ModelName)
            .ToList();
        return new GateResult(failing.Count == 0, threshold, failing);
    }
}
