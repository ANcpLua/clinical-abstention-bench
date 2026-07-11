using System.Globalization;
using ClinicalAbstentionBench;

// clinical-abstention-bench — selective prediction on clinical vignettes.
//
// Each case is shown twice: once with the decisive finding present (the model SHOULD answer)
// and once with it removed (the model SHOULD abstain). We score answering-when-supported
// against answering-when-unsupported.
//
// Fail-closed, mirroring the ancplua.evaluation engine: the run exits 0 only when every item
// was scored and the report was written. A requested-but-unavailable model is an ERROR (exit 1),
// never a silent skip. An optional --gate <recall> also fails the run if any model IN THE RUN has
// an abstention recall below the threshold — use --only / --no-baselines to point it at one model.

var opts = Args.Parse(args);

try
{
    return opts.Mode switch
    {
        "demo" => await RunBenchAsync(opts),
        "ollama" => await RunBenchAsync(opts, new OllamaModel(opts.Model ?? "llama3.2:3b")),
        "llm" => RunLlm(opts),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1; // fail closed
}

async Task<int> RunBenchAsync(Args o, IModel? liveModel = null)
{
    var dataDir = Bench.FindDataDir(o.DataDir);
    var cases = Bench.LoadCases(dataDir);
    var items = Bench.ItemsFor(cases);

    var available = new List<IModel>();
    if (!o.NoBaselines) available.AddRange(Bench.LoadDemoModels(dataDir));
    if (liveModel is not null) available.Add(liveModel);

    var models = Bench.SelectModels(available, o.Only);
    if (models.Count == 0)
        throw new InvalidOperationException(
            "No models selected. --no-baselines removed every model from a 'demo' run — use 'ollama' to add a live model, or drop the flag.");

    Console.WriteLine($"clinical-abstention-bench · {cases.Count} cases → {items.Count} items · {models.Count} models\n");

    var cards = new List<Scorecard>();
    var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>();
    foreach (var model in models)
    {
        var results = await Bench.RunModelAsync(model, items);
        resultsByModel[model.Name] = results;
        cards.Add(Scorecard.From(model.Name, results));
    }

    PrintTable(cards);

    var report = Report.Build(o.Mode, cases.Count, items.Count, models, resultsByModel, cards, DateTimeOffset.UtcNow);
    Report.Write(o.OutPath, report);
    Console.WriteLine($"report → {o.OutPath}  ({report.Transcripts.Count} per-item transcripts)");

    if (o.HtmlPath is { } htmlPath)
    {
        File.WriteAllText(htmlPath, ScorecardPage.Render(cases.Count, items.Count, cards, cases[0]));
        Console.WriteLine($"html report → {htmlPath}");
    }

    if (o.Gate is { } threshold)
    {
        var gate = Gate.Check(cards, threshold);
        if (!gate.Passed)
        {
            Console.Error.WriteLine(
                $"\nGATE FAILED: abstention recall < {threshold:P0} for: {string.Join(", ", gate.FailingModels)}");
            return 1;
        }
        Console.WriteLine($"\nGATE PASSED: every model in this run is ≥ {threshold:P0} abstention recall.");
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
    Console.WriteLine($"{"model",-22} {"abstain-recall",14} {"unsupported",14} {"answer-acc",14} {"over-abstain",14} {"selective-acc",14}");
    Console.WriteLine(new string('─', 97));
    foreach (var c in cards)
        Console.WriteLine($"{c.ModelName,-22} {c.AbstentionRecall,14} {c.UnsupportedAnswerRate,14} {c.AnswerAccuracy,14} {c.OverAbstentionRate,14} {c.SelectiveAccuracy,14}");
    Console.WriteLine();
    Console.WriteLine("abstain-recall: of the must-abstain (ablated) items, how many the model correctly declined.");
    Console.WriteLine("unsupported:    of the must-abstain items, how many it answered anyway — the failure mode this benchmark targets.");
    Console.WriteLine("cells are percentages; [low–high] is the 95 % Wilson score interval. At n = 12 these are wide —");
    Console.WriteLine("overlapping intervals mean the models are not distinguishable, however far apart the point estimates look.");
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
          --gate  <0..1>   fail the run if ANY model in it has abstention recall below this.
                           Combine with --only / --no-baselines: AlwaysAnswerBaseline has 0 %
                           recall by construction, so an unfiltered run can never pass a gate.
          --only  <name>   run only this model (repeatable; case-insensitive; unknown name = error)
          --no-baselines   drop the deterministic fixture models from the run
          --out   <file>   report path (default: report.json)
          --html  <file>   also write a self-contained HTML report
          --model <name>   ollama model tag (default: llama3.2:3b)

        examples:
          dotnet run -- demo --only CalibratedBaseline --gate 0.9
          dotnet run -- ollama --model llama3.2:3b --no-baselines --gate 0.9
        """);
    return 0;
}

/// Tiny arg holder.
file sealed record Args(
    string Mode,
    string? DataDir,
    double? Gate,
    string OutPath,
    string? HtmlPath,
    string? Model,
    IReadOnlyList<string> Only,
    bool NoBaselines)
{
    public static Args Parse(string[] argv)
    {
        var mode = argv.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "demo";
        string? dataDir = null;
        double? gate = null;
        var outPath = "report.json";
        string? htmlPath = null;
        string? model = null;
        var only = new List<string>();
        var noBaselines = false;

        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--data" when i + 1 < argv.Length: dataDir = argv[++i]; break;
                case "--out" when i + 1 < argv.Length: outPath = argv[++i]; break;
                case "--html" when i + 1 < argv.Length: htmlPath = argv[++i]; break;
                case "--model" when i + 1 < argv.Length: model = argv[++i]; break;
                case "--only" when i + 1 < argv.Length: only.Add(argv[++i]); break;
                case "--no-baselines": noBaselines = true; break;
                case "--gate" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var g):
                    gate = g; break;
            }
        }
        return new Args(mode, dataDir, gate, outPath, htmlPath, model, only, noBaselines);
    }
}
