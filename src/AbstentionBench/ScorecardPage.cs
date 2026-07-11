using System.Globalization;
using System.Net;
using System.Text;

namespace ClinicalAbstentionBench;

/// Dependency-free HTML rendering of the structured v2 scorecard.
public static class ScorecardPage
{
    public static string Render(
        int caseCount,
        int itemCount,
        IReadOnlyList<Scorecard> cards,
        BenchCase example,
        IReadOnlySet<string> baselineNames)
    {
        var primaryRows = Rows(
            cards.OrderByDescending(card => card.DecisionAccuracy.Value),
            baselineNames,
            card =>
                $"<td>{Cell(card.Coverage)}</td>" +
                $"<td class=\"good\">{Cell(card.SelectiveAccuracy)}</td>" +
                $"<td class=\"good\">{Cell(card.DecisionAccuracy)}</td>" +
                $"<td>{Cell(card.CertaintyAccuracy)}</td>" +
                $"<td>{Cell(card.UrgencyAccuracy)}</td>" +
                $"<td class=\"bad\">{Cell(card.UndertriageRate)}</td>");

        var contrastRows = Rows(
            cards.OrderByDescending(card => card.PairedRevisionAccuracy.Value),
            baselineNames,
            card =>
                $"<td class=\"good\">{Cell(card.ContrastAccuracy)}</td>" +
                $"<td class=\"bad\">{Cell(card.OriginalTargetPersistence)}</td>" +
                $"<td class=\"good\">{Cell(card.PairedRevisionAccuracy)}</td>" +
                $"<td>{Cell(card.ContrastCertaintyAccuracy)}</td>" +
                $"<td>{Cell(card.ContrastUrgencyAccuracy)}</td>" +
                $"<td class=\"bad\">{Cell(card.ContrastUndertriageRate)}</td>");

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>clinical-abstention-bench report</title>
            <style>
              :root { color-scheme:dark; }
              body { background:#0b0f14; color:#e6edf3; font:15px/1.5 -apple-system,'Segoe UI',sans-serif;
                     max-width:1080px; margin:0 auto; padding:44px 28px; }
              h1 { margin:0; font-size:28px; } h2 { margin:34px 0 8px; font-size:18px; }
              .tag,.note,.counts { color:#9aa7b4; } .counts { font-size:12px; letter-spacing:.08em;
                     text-transform:uppercase; margin:24px 0 8px; }
              table { border-collapse:collapse; width:100%; margin:8px 0 18px; }
              th,td { text-align:right; padding:9px 11px; border-bottom:1px solid #1c2530;
                      font-variant-numeric:tabular-nums; }
              th { color:#9aa7b4; font-size:11px; text-transform:uppercase; letter-spacing:.05em; }
              th:first-child,td:first-child { text-align:left; }
              td:first-child { font-weight:600; } .good { color:#51cf66; } .bad { color:#ff6b6b; }
              .ci { display:block; color:#7d8894; font-size:10px; }
              tr.baseline td { opacity:.64; } tr.live td:first-child { border-left:3px solid #4c8dff; }
              .kind { display:block; color:#7d8894; font-size:10px; font-weight:400; }
              .variants { display:grid; grid-template-columns:repeat(auto-fit,minmax(260px,1fr)); gap:14px; }
              .card { background:#111823; border:1px solid #1c2530; border-radius:9px; padding:15px; }
              .card h3 { margin:0 0 8px; color:#9aa7b4; font-size:11px; letter-spacing:.06em;
                         text-transform:uppercase; }
              .target { margin-top:12px; color:#51cf66; font-weight:600; }
              code { color:#d0d7de; }
            </style>
            </head>
            <body>
              <h1>clinical-abstention-bench</h1>
              <p class="tag">Structured diagnosis, certainty, and urgency under changing clinical evidence.</p>
              <div class="counts">{{caseCount}} cases · {{itemCount}} items · {{cards.Count}} models</div>
              <table>
                <thead><tr><th>model</th><th>coverage</th><th>selective acc</th><th>decision acc</th>
                  <th>certainty acc</th><th>urgency acc</th><th>undertriage</th></tr></thead>
                <tbody>{{primaryRows}}</tbody>
              </table>
              <p class="note"><strong>Selective accuracy</strong> is diagnostic accuracy among answered
              full + ablated items; read it with coverage. Decision accuracy additionally credits a correct
              diagnostic deferral. Diagnosis and urgency are scored independently.</p>

              <h2>Alternative-supported contrast</h2>
              <table>
                <thead><tr><th>model</th><th>contrast acc</th><th>original persists</th><th>paired revision</th>
                  <th>certainty acc</th><th>urgency acc</th><th>undertriage</th></tr></thead>
                <tbody>{{contrastRows}}</tbody>
              </table>
              <p class="note">Contrasts support a determinate alternative. Silence and arbitrary alternatives
              no longer count as evidence sensitivity: paired revision requires both diagnoses to be correct and
              the contrast concept not to be supported by the full state.</p>

              <h2>Example — {{E(example.Condition)}}</h2>
              <div class="variants">
                {{VariantCard("full", example.Full)}}
                {{VariantCard("ablated", example.Ablated)}}
                {{VariantCard("contrast", example.Contrast)}}
              </div>
              <p class="note">Rates include 95% Wilson intervals. Dimmed rows are deterministic reference
              policies generated from benchmark targets; they are controls, not model competitors.</p>
            </body>
            </html>
            """;
    }

    private static string Rows(
        IEnumerable<Scorecard> cards,
        IReadOnlySet<string> baselineNames,
        Func<Scorecard, string> cells)
    {
        var rows = new StringBuilder();
        foreach (var card in cards)
        {
            var baseline = baselineNames.Contains(card.ModelName);
            var kind = baseline
                ? "<span class=\"kind\">programmatic reference · no system prompt</span>"
                : "";
            rows.Append($"<tr class=\"{(baseline ? "baseline" : "live")}\"><td>{E(card.ModelName)}{kind}</td>");
            rows.Append(cells(card));
            rows.AppendLine("</tr>");
        }
        return rows.ToString();
    }

    private static string VariantCard(string label, CaseVariant variant)
    {
        var diagnosis = variant.Target.Diagnosis is null ? "diagnostic deferral" : E(variant.Target.Diagnosis);
        return $"""
            <div class="card"><h3>{E(label)}</h3>
              {E(variant.Vignette)}
              <div class="target">→ {diagnosis} · {E(variant.Target.DiagnosticStatus.WireName())}
              · {E(variant.Target.Urgency.WireName())}</div>
            </div>
            """;
    }

    private static string Cell(Rate rate)
        => rate.Total == 0
            ? "n/a"
            : $"{Percent(rate.Value)}<span class=\"ci\">[{Percent(rate.Lower)}–{Percent(rate.Upper)}]</span>";

    private static string Percent(double value)
        => Math.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
