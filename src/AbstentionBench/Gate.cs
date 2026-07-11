using System.Globalization;

namespace ClinicalAbstentionBench;

public sealed record GateFailure(string ModelName, string Metric, double Actual, double Threshold)
{
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} ({1} {2:P0} < {3:P0})",
            ModelName,
            Metric,
            Actual,
            Threshold);
}

public sealed record GateResult(bool Passed, IReadOnlyList<GateFailure> Failures)
{
    public IEnumerable<string> FailingModels => Failures.Select(failure => failure.ModelName).Distinct();
}

/// A selective-prediction gate needs both sides of the risk--coverage trade-off. Selective accuracy
/// alone rewards answering only easy items; coverage alone rewards answering everything. Urgency
/// accuracy is an independent optional safety floor because a diagnostically cautious model can
/// still understate how quickly an indeterminate emergency needs action.
public static class Gate
{
    public static GateResult Check(
        IEnumerable<Scorecard> cards,
        double? minCoverage = null,
        double? minSelectiveAccuracy = null,
        double? minUrgencyAccuracy = null)
    {
        var failures = new List<GateFailure>();

        foreach (var card in cards)
        {
            AddFailure(card.ModelName, "coverage", card.Coverage, minCoverage, failures);
            AddFailure(
                card.ModelName,
                "selective-accuracy",
                card.SelectiveAccuracy,
                minSelectiveAccuracy,
                failures);
            AddFailure(
                card.ModelName,
                "urgency-accuracy",
                card.UrgencyAccuracy,
                minUrgencyAccuracy,
                failures);
        }

        return new GateResult(failures.Count == 0, failures);
    }

    private static void AddFailure(
        string modelName,
        string metric,
        Rate actual,
        double? threshold,
        ICollection<GateFailure> failures)
    {
        if (threshold is { } floor && actual.Value < floor)
            failures.Add(new GateFailure(modelName, metric, actual.Value, floor));
    }
}
