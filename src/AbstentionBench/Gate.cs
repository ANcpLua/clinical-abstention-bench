using System.Globalization;

namespace ClinicalAbstentionBench;

/// Why a model failed the gate. A gate that only checked abstention is one a mute model passes.
public sealed record GateFailure(string ModelName, string Metric, double Actual, double Threshold)
{
    /// Invariant, not the ambient culture: this string is what a CI log shows and what an operator
    /// greps for. It must not change shape because the runner happened to boot in a German locale.
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "{0} ({1} {2:P0} < {3:P0})", ModelName, Metric, Actual, Threshold);
}

public sealed record GateResult(bool Passed, IReadOnlyList<GateFailure> Failures)
{
    public IEnumerable<string> FailingModels => Failures.Select(f => f.ModelName).Distinct();
}

/// The CI gate, kept out of the CLI so it is unit-testable.
///
/// Two thresholds, and the second one is not optional decoration. **Abstention-recall is trivially
/// maximised by never answering**: `AlwaysAbstainBaseline` scores 100 % on it while being useless.
/// A gate on recall alone is therefore passed by a model that has learned to say nothing — so the
/// answer-accuracy floor exists to make the gate mean "knows when to speak AND knows the medicine".
///
/// The gate applies to exactly the models that were run, which makes model selection (`--only`,
/// `--no-baselines`) gate selection too: an unfiltered run always contains AlwaysAnswerBaseline,
/// whose recall is 0 by construction, so it could never pass.
public static class Gate
{
    public static GateResult Check(
        IEnumerable<Scorecard> cards,
        double? minAbstentionRecall = null,
        double? minAnswerAccuracy = null)
    {
        var failures = new List<GateFailure>();

        foreach (var card in cards)
        {
            if (minAbstentionRecall is { } recall && card.AbstentionRecall.Value < recall)
                failures.Add(new GateFailure(card.ModelName, "abstention-recall", card.AbstentionRecall.Value, recall));

            if (minAnswerAccuracy is { } accuracy && card.AnswerAccuracy.Value < accuracy)
                failures.Add(new GateFailure(card.ModelName, "answer-accuracy", card.AnswerAccuracy.Value, accuracy));
        }

        return new GateResult(failures.Count == 0, failures);
    }
}
