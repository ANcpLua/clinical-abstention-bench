namespace ClinicalAbstentionBench;

/// What happened on one item.
///  - CorrectAnswer / WrongAnswer / OverAbstention apply to answerable (Full) items.
///  - CorrectAbstention / UnsupportedAnswer apply to must-abstain (Ablated) items.
public enum Outcome
{
    CorrectAnswer,
    WrongAnswer,
    OverAbstention,
    CorrectAbstention,
    UnsupportedAnswer
}

public static class Scoring
{
    // Surface markers that count as the model declining to answer. Deliberately lightweight for v0;
    // a semantic / LLM-judge grader is a v1 upgrade (see TASK.md).
    private static readonly string[] AbstentionMarkers =
    [
        "insufficient", "cannot be determined", "cannot determine", "can't determine",
        "not enough information", "unable to determine", "cannot be established",
        "indeterminate", "not determinable", "need more information",
        "more information is needed", "nicht bestimmbar", "unbestimmbar"
    ];

    public static bool IsAbstention(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        var r = response.ToLowerInvariant();
        foreach (var marker in AbstentionMarkers)
            if (r.Contains(marker, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsCorrect(string response, string expected)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(expected)) return false;
        return response.ToLowerInvariant().Contains(expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    public static Outcome Score(Item item, string response)
    {
        var abstained = IsAbstention(response);

        if (item.MustAbstain)
            return abstained ? Outcome.CorrectAbstention : Outcome.UnsupportedAnswer;

        if (abstained) return Outcome.OverAbstention;
        return IsCorrect(response, item.GroundTruth) ? Outcome.CorrectAnswer : Outcome.WrongAnswer;
    }
}

/// One item, one model, one reply — the auditable unit. `SystemPrompt` is the prompt that was in
/// force when the reply was produced (null for a fixture, which never sees one), so a transcript
/// entry is self-contained: everything needed to reproduce the score is on it.
public sealed record ItemResult(
    string ModelName,
    Item Item,
    string? SystemPrompt,
    string Response,
    Outcome Outcome);

/// Aggregated per-model result. The headline is AbstentionRecall / UnsupportedAnswerRate on
/// the must-abstain half — i.e. does the model decline once the decisive finding is removed?
public sealed record Scorecard(
    string ModelName,
    int AblatedTotal, int CorrectAbstentions, int UnsupportedAnswers,
    int FullTotal, int CorrectAnswers, int WrongAnswers, int OverAbstentions)
{
    public double AbstentionRecall       => AblatedTotal == 0 ? 0 : (double)CorrectAbstentions / AblatedTotal;
    public double UnsupportedAnswerRate  => AblatedTotal == 0 ? 0 : (double)UnsupportedAnswers / AblatedTotal;
    public double AnswerAccuracy         => FullTotal == 0 ? 0 : (double)CorrectAnswers / FullTotal;
    public double OverAbstentionRate     => FullTotal == 0 ? 0 : (double)OverAbstentions / FullTotal;

    /// Fraction of all items where the model's output matched what the evidence supported:
    /// a correct answer when the case was answerable, an abstention when it was not.
    public double SelectiveAccuracy
    {
        get
        {
            var total = AblatedTotal + FullTotal;
            return total == 0 ? 0 : (double)(CorrectAbstentions + CorrectAnswers) / total;
        }
    }

    public static Scorecard From(string model, IEnumerable<ItemResult> results)
    {
        int at = 0, ca = 0, ua = 0, ft = 0, cans = 0, wr = 0, oa = 0;
        foreach (var r in results)
        {
            if (r.Item.MustAbstain)
            {
                at++;
                if (r.Outcome == Outcome.CorrectAbstention) ca++; else ua++;
            }
            else
            {
                ft++;
                switch (r.Outcome)
                {
                    case Outcome.CorrectAnswer: cans++; break;
                    case Outcome.WrongAnswer: wr++; break;
                    case Outcome.OverAbstention: oa++; break;
                }
            }
        }
        return new Scorecard(model, at, ca, ua, ft, cans, wr, oa);
    }
}
