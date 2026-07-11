using System.Globalization;
using System.Net;
using System.Text;

namespace ClinicalAbstentionBench;

/// Renders the scorecards as a single self-contained dark-themed HTML page —
/// the benchmark's real output, suitable for sharing or screenshotting.
/// Deliberately dependency-free: inline CSS, no scripts, no CDN.
public static class ScorecardPage
{
    public static string Render(
        int caseCount,
        int itemCount,
        IReadOnlyList<Scorecard> cards,
        BenchCase example,
        IReadOnlySet<string> baselineNames)
    {
        var rows = new StringBuilder();
        foreach (var c in cards.OrderByDescending(c => c.SelectiveAccuracy.Value))
        {
            rows.AppendLine($"""
                <tr class="{RowClass(c, baselineNames)}">
                  <td class="model">{E(c.ModelName)}{BaselineTag(c, baselineNames)}</td>
                  <td>{Cell(c.AbstentionRecall)}</td>
                  <td class="unsupported">{Cell(c.UnsupportedAnswerRate)}</td>
                  <td>{Cell(c.AnswerAccuracy)}</td>
                  <td>{Cell(c.OverAbstentionRate)}</td>
                  <td class="selective">{Cell(c.SelectiveAccuracy)}</td>
                </tr>
                """);
        }

        var probe = Probe(cards, baselineNames);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>clinical-abstention-bench report</title>
            <style>
              :root { color-scheme: dark; }
              body { background:#0b0f14; color:#e6edf3; font:16px/1.55 -apple-system,'Segoe UI',sans-serif;
                     max-width:880px; margin:0 auto; padding:48px 32px; }
              h1 { font-size:28px; margin:0; letter-spacing:-.02em; }
              p.tag { color:#9aa7b4; margin:6px 0 28px; }
              .counts { color:#9aa7b4; font-size:13px; margin-bottom:10px; text-transform:uppercase; letter-spacing:.08em; }
              table { border-collapse:collapse; width:100%; margin:8px 0 34px; }
              th,td { text-align:right; padding:10px 14px; border-bottom:1px solid #1c2530; font-variant-numeric:tabular-nums; }
              th { color:#9aa7b4; font-size:12px; text-transform:uppercase; letter-spacing:.06em; font-weight:600; }
              td.model, th.model { text-align:left; font-weight:600; }
              td.unsupported { color:#ff6b6b; font-weight:700; }
              td.selective { color:#51cf66; font-weight:700; }
              th.unsupported-h { color:#ff6b6b; }
              .note { color:#9aa7b4; font-size:13px; }
              .ci { display:block; color:#7d8894; font-size:11px; font-weight:400; margin-top:2px; }
              td.unsupported .ci, td.selective .ci { color:#7d8894; }
              tr.baseline td { opacity:.62; }
              tr.live td.model { border-left:3px solid #4c8dff; padding-left:11px; }
              .tagline { display:block; color:#7d8894; font-size:11px; font-weight:400; margin-top:2px; }
              .triple { display:grid; grid-template-columns:repeat(auto-fit,minmax(230px,1fr)); gap:14px; margin-top:10px; }
              .card { background:#111823; border:1px solid #1c2530; border-radius:10px; padding:16px 18px; font-size:14px; }
              .card h3 { margin:0 0 8px; font-size:12px; text-transform:uppercase; letter-spacing:.08em; }
              .full h3 { color:#51cf66; } .ablated h3 { color:#ff6b6b; } .counterfactual h3 { color:#ffa94d; }
              .ans { margin-top:10px; font-weight:700; }
              .full .ans { color:#51cf66; } .ablated .ans { color:#ff6b6b; } .counterfactual .ans { color:#ffa94d; }
              .ans small { display:block; margin-top:6px; font-weight:400; color:#9aa7b4; font-size:12px; }
              h2 { font-size:16px; margin:28px 0 4px; }
            </style>
            </head>
            <body>
              <h1>clinical-abstention-bench</h1>
              <p class="tag">Selective prediction on clinical vignettes — does the model abstain when the evidence is insufficient, rather than only scoring accuracy?</p>
              <div class="counts">{{caseCount}} cases · {{itemCount}} items · {{cards.Count}} models</div>
              <table>
                <thead><tr>
                  <th class="model">model</th><th>abstain-recall</th><th class="unsupported-h">unsupported</th>
                  <th>answer-acc</th><th>over-abstain</th><th>selective-acc</th>
                </tr></thead>
                <tbody>
            {{rows}}
                </tbody>
              </table>
              <p class="note">unsupported — of the items where the decisive finding was removed, the fraction the model answered anyway. This is the failure mode the benchmark targets.</p>
              <p class="note">Brackets are the <strong>95 % Wilson score interval</strong>. With {{caseCount}} cases they are wide: where two models' intervals overlap, this benchmark cannot tell them apart, however far apart the headline percentages look.</p>
              <p class="note">Dimmed rows are deterministic <strong>reference policies</strong>. They never see a system prompt; provenance declares any label access, and the label oracle is perfect by construction. They are analytical reference points rather than competitors.</p>
              <p class="note">A live model's row is named <code>model @ prompt</code>. Abstention is <strong>prompt-sensitive</strong>: a rate measured under one system prompt is a claim about that prompt-and-model pair, not about the model. Sweep them with <code>--prompt all</code>.</p>
            {{probe}}
              <h2>How a case works — {{E(example.Condition)}}</h2>
              <div class="triple">
                <div class="card full"><h3>full — decisive finding present</h3>{{E(example.FullPrompt)}}<div class="ans">→ answer: {{E(example.ExpectedAnswer)}}</div></div>
                <div class="card ablated"><h3>ablated — {{E(example.RemovedFact)}} removed</h3>{{E(example.AblatedPrompt)}}<div class="ans">→ supported reply: INSUFFICIENT</div></div>
                <div class="card counterfactual"><h3>counterfactual — that finding flipped</h3>{{E(example.CounterfactualPrompt)}}<div class="ans">→ supported reply: INSUFFICIENT<br><small>saying “{{E(example.ExpectedAnswer)}}” here means the finding was never read</small></div></div>
              </div>
            </body>
            </html>
            """;
    }

    /// The counterfactual arm gets its own table, deliberately. It is a probe — "did the model read
    /// the decisive finding?" — and not part of selective accuracy, because like abstention-recall it
    /// is trivially maximised by a model that answers nothing.
    private static string Probe(IReadOnlyList<Scorecard> cards, IReadOnlySet<string> baselineNames)
    {
        if (cards.All(c => c.CounterfactualTotal == 0)) return "";

        var rows = new StringBuilder();
        foreach (var c in cards.OrderByDescending(c => c.EvidenceSensitivity.Value))
        {
            rows.AppendLine($"""
                <tr class="{RowClass(c, baselineNames)}">
                  <td class="model">{E(c.ModelName)}{BaselineTag(c, baselineNames)}</td>
                  <td class="selective">{Cell(c.EvidenceSensitivity)}</td>
                  <td class="unsupported">{Cell(c.EvidenceInsensitivityRate)}</td>
                  <td>{Cell(new Rate(c.CounterfactualAbstentions, c.CounterfactualTotal))}</td>
                </tr>
                """);
        }

        return $"""
              <h2>Counterfactual probe — did the model read the finding at all?</h2>
              <p class="note">The decisive finding is not removed here; it is <strong>flipped</strong>, so that it now
              <em>excludes</em> the original diagnosis. A model that names that diagnosis anyway cannot have read the
              finding — the finding says no. This is what separates a model that reads the evidence and is overconfident
              from one that pattern-matches the shape of the vignette; on the scorecard above, those two are identical.</p>
              <table>
                <thead><tr>
                  <th class="model">model</th><th>evidence-sensitivity</th>
                  <th class="unsupported-h">said the excluded diagnosis</th><th>abstained</th>
                </tr></thead>
                <tbody>
            {rows}    </tbody>
              </table>
              <p class="note">Not folded into selective-accuracy: like abstention-recall, this is trivially maximised by a model that answers nothing.</p>
            """;
    }

    /// Baseline rows are dimmed and tagged. They are deterministic, programmatic policies that never
    /// see a system prompt, so the page must not present them as like-for-like competitors.
    private static string RowClass(Scorecard c, IReadOnlySet<string> baselineNames)
        => baselineNames.Contains(c.ModelName) ? "baseline" : "live";

    private static string BaselineTag(Scorecard c, IReadOnlySet<string> baselineNames)
        => baselineNames.Contains(c.ModelName) ? """<span class="tagline">programmatic reference · no system prompt</span>""" : "";

    /// A rate is never shown without its interval. At n = 12 the point estimate on its own is the
    /// most misleading thing this page could print.
    private static string Cell(Rate r)
        => $"""{Pct(r.Value)}<span class="ci">[{Pct(r.Lower)}–{Pct(r.Upper)}]</span>""";

    private static string Pct(double v)
        => Math.Round(v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
