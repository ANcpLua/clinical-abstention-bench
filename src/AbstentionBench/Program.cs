using System.Globalization;
using System.Text.Json;
using ClinicalAbstentionBench;

// clinical-abstention-bench — selective prediction on clinical vignettes.
//
// Each case is shown twice: once with the decisive finding present (the model SHOULD answer)
// and once with it removed (the model SHOULD abstain). We score answering-when-supported
// against answering-when-unsupported.
//
// Fail-closed, mirroring the ancplua.evaluation engine: the run exits 0 only when every item
// was scored and the report was written. A requested-but-unavailable model is an ERROR (exit 1),
// never a silent skip. An optional --gate <recall> also fails the run if a model's abstention
// recall falls below the threshold.

var opts = Args.Parse(args);

try
{
    return opts.Mode switch
    {
        "demo" => await RunDemoAsync(opts),
        "ollama" => await RunDemoAsync(opts, new OllamaModel(opts.Model ?? "llama3.2:3b")),
        "llm" => RunLlm(opts),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1; // fail closed
}

async Task<int> RunDemoAsync(Args o, IModel? liveModel = null)
{
    var dataDir = Bench.FindDataDir(o.DataDir);
    var cases = Bench.LoadCases(dataDir);
    var items = Bench.ItemsFor(cases);
    var models = Bench.LoadDemoModels(dataDir);
    if (liveModel is not null) models.Add(liveModel);

    Console.WriteLine($"clinical-abstention-bench · {cases.Count} cases → {items.Count} items · {models.Count} models\n");

    var cards = new List<Scorecard>();
    foreach (var model in models)
    {
        var results = await Bench.RunModelAsync(model, items);
        cards.Add(Scorecard.From(model.Name, results));
    }

    PrintTable(cards);
    WriteReport(o.OutPath, cases.Count, items.Count, cards);
    if (o.HtmlPath is { } htmlPath)
    {
        File.WriteAllText(htmlPath, ScorecardPage.Render(cases.Count, items.Count, cards, cases[0]));
        Console.WriteLine($"html report → {htmlPath}");
    }

    if (o.Gate is { } threshold)
    {
        var failing = cards.Where(c => c.AbstentionRecall < threshold).ToList();
        if (failing.Count > 0)
        {
            Console.Error.WriteLine(
                $"\nGATE FAILED: abstention recall < {threshold:P0} for: {string.Join(", ", failing.Select(f => f.ModelName))}");
            return 1;
        }
        Console.WriteLine($"\nGATE PASSED: every model ≥ {threshold:P0} abstention recall.");
    }

    return 0;
}

int RunLlm(Args o)
{
    // Live adapter (Anthropic / OpenAI via Microsoft.Extensions.AI) lands in v1. Until then the
    // 'llm' mode fails closed rather than pretending — matching the engine's "missing credential = ERROR".
    var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrEmpty(key))
    {
        Console.Error.WriteLine("ERROR: 'llm' needs ANTHROPIC_API_KEY (or OPENAI_API_KEY). Failing closed — run 'demo' for the no-credential path.");
        return 1;
    }
    Console.Error.WriteLine("ERROR: the live LLM adapter is not wired yet (v1). Run 'demo' for now — see TASK.md.");
    return 1;
}

static void PrintTable(IReadOnlyList<Scorecard> cards)
{
    Console.WriteLine($"{"model",-22} {"abstain-recall",15} {"unsupported",12} {"answer-acc",12} {"over-abstain",13} {"selective-acc",14}");
    Console.WriteLine(new string('─', 93));
    foreach (var c in cards)
        Console.WriteLine($"{c.ModelName,-22} {Pct(c.AbstentionRecall),15} {Pct(c.UnsupportedAnswerRate),12} {Pct(c.AnswerAccuracy),12} {Pct(c.OverAbstentionRate),13} {Pct(c.SelectiveAccuracy),14}");
    Console.WriteLine();
    Console.WriteLine("abstain-recall: of the must-abstain (ablated) items, how many the model correctly declined.");
    Console.WriteLine("unsupported:    of the must-abstain items, how many it answered anyway — the failure mode this benchmark targets.");
}

static string Pct(double v) => v.ToString("P0", CultureInfo.InvariantCulture);

static void WriteReport(string outPath, int caseCount, int itemCount, IReadOnlyList<Scorecard> cards)
{
    var report = new
    {
        cases = caseCount,
        items = itemCount,
        models = cards.Select(c => new
        {
            c.ModelName,
            c.AbstentionRecall, c.UnsupportedAnswerRate, c.AnswerAccuracy, c.OverAbstentionRate, c.SelectiveAccuracy,
            c.AblatedTotal, c.CorrectAbstentions, c.UnsupportedAnswers,
            c.FullTotal, c.CorrectAnswers, c.WrongAnswers, c.OverAbstentions
        })
    };
    File.WriteAllText(outPath, JsonSerializer.Serialize(report, Bench.Json));
    Console.WriteLine($"report → {outPath}");
}

static int Usage()
{
    Console.WriteLine("""
        clinical-abstention-bench

        usage:
          dotnet run -- demo              run the offline baseline models (default, no credentials)
          dotnet run -- ollama            baselines + a real local LLM via Ollama (default llama3.2:3b)
          dotnet run -- llm               run a live cloud model (needs ANTHROPIC_API_KEY; v1)

        flags:
          --data  <dir>    path to the data/ folder (auto-detected by default)
          --gate  <0..1>   fail the run if any model's abstention recall is below this
          --out   <file>   report path (default: report.json)
          --html  <file>   also write a self-contained HTML report
          --model <name>   ollama model tag (default: llama3.2:3b)
        """);
    return 0;
}

/// Tiny arg holder.
file sealed record Args(string Mode, string? DataDir, double? Gate, string OutPath, string? HtmlPath, string? Model)
{
    public static Args Parse(string[] argv)
    {
        var mode = argv.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "demo";
        string? dataDir = null;
        double? gate = null;
        var outPath = "report.json";
        string? htmlPath = null;
        string? model = null;

        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--data" when i + 1 < argv.Length: dataDir = argv[++i]; break;
                case "--out" when i + 1 < argv.Length: outPath = argv[++i]; break;
                case "--html" when i + 1 < argv.Length: htmlPath = argv[++i]; break;
                case "--model" when i + 1 < argv.Length: model = argv[++i]; break;
                case "--gate" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var g):
                    gate = g; break;
            }
        }
        return new Args(mode, dataDir, gate, outPath, htmlPath, model);
    }
}
