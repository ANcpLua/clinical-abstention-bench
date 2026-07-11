using System.Globalization;
using System.Net;
using System.Text;

namespace ClinicalAbstentionBench;

/// Renders the scorecards as a single self-contained dark-themed HTML page —
/// the benchmark's real output, suitable for sharing or screenshotting.
/// Deliberately dependency-free: inline CSS, no scripts, no CDN.
public static class ScorecardPage
{
    public static string Render(int caseCount, int itemCount, IReadOnlyList<Scorecard> cards, BenchCase example)
    {
        var rows = new StringBuilder();
        foreach (var c in cards.OrderByDescending(c => c.SelectiveAccuracy))
        {
            rows.AppendLine($"""
                <tr>
                  <td class="model">{E(c.ModelName)}</td>
                  <td>{Pct(c.AbstentionRecall)}</td>
                  <td class="unsupported">{Pct(c.UnsupportedAnswerRate)}</td>
                  <td>{Pct(c.AnswerAccuracy)}</td>
                  <td>{Pct(c.OverAbstentionRate)}</td>
                  <td class="selective">{Pct(c.SelectiveAccuracy)}</td>
                </tr>
                """);
        }

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
              .pair { display:grid; grid-template-columns:1fr 1fr; gap:14px; margin-top:10px; }
              .card { background:#111823; border:1px solid #1c2530; border-radius:10px; padding:16px 18px; font-size:14px; }
              .card h3 { margin:0 0 8px; font-size:12px; text-transform:uppercase; letter-spacing:.08em; }
              .full h3 { color:#51cf66; } .ablated h3 { color:#ff6b6b; }
              .ans { margin-top:10px; font-weight:700; }
              .full .ans { color:#51cf66; } .ablated .ans { color:#ff6b6b; }
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
              <p class="note">Baseline rows are deterministic fixtures keyed on item id; they never see a system prompt, so they are not directly comparable to a live model.</p>
              <h2>How a case works — {{E(example.Condition)}}</h2>
              <div class="pair">
                <div class="card full"><h3>full — decisive finding present</h3>{{E(example.FullPrompt)}}<div class="ans">→ answer: {{E(example.ExpectedAnswer)}}</div></div>
                <div class="card ablated"><h3>ablated — {{E(example.RemovedFact)}} removed</h3>{{E(example.AblatedPrompt)}}<div class="ans">→ supported reply: INSUFFICIENT</div></div>
              </div>
            </body>
            </html>
            """;
    }

    private static string Pct(double v) => v.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", " ");

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
