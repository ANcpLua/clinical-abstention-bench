using System.Globalization;
using ClinicalAbstentionBench;

// clinical-abstention-bench — selective prediction on clinical vignettes.
//
// Each case is shown three ways: with the decisive finding present (answer), removed (abstain),
// and flipped to exclude the original diagnosis (abstain and test evidence sensitivity).
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
        "demo" => await RunBenchAsync(opts, live: false),
        "ollama" => await RunBenchAsync(opts, live: true),
        _ => Usage(opts.Mode)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1; // fail closed
}

async Task<int> RunBenchAsync(Args o, bool live)
{
    var dataDir = Bench.FindDataDir(o.DataDir);
    var cases = Bench.LoadCases(dataDir);
    var items = Bench.ItemsFor(cases);
    var prompts = Bench.SelectPrompts(Bench.LoadPrompts(dataDir), o.Prompts);

    var available = new List<IModel>();
    if (!o.NoBaselines) available.AddRange(Bench.CreateReferenceModels(cases));

    // One run per (model, prompt): the system prompt is a controlled variable, so sweeping it is how
    // you find out how much of an unsupported-answer rate belongs to the prompt and not to the model.
    if (live)
        available.AddRange(prompts.Select(p => new OllamaModel(o.Model ?? "llama3.2:3b", p)));

    var models = Bench.SelectModels(available, o.Only);
    if (models.Count == 0)
        throw new InvalidOperationException(
            "No models selected. --no-baselines removed every model from a 'demo' run — use 'ollama' to add a live model, or drop the flag.");

    var promptNote = live && prompts.Count > 1 ? $" · {prompts.Count} system prompts" : "";
    Console.WriteLine($"clinical-abstention-bench · {cases.Count} cases → {items.Count} items · {models.Count} models{promptNote}\n");

    var cards = new List<Scorecard>();
    var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>();
    foreach (var model in models)
    {
        var results = await Bench.RunModelAsync(model, items);
        resultsByModel[model.Name] = results;
        cards.Add(Scorecard.From(model.Name, results));
    }

    PrintTable(cards, models);

    var report = Report.Build(o.Mode, cases.Count, items.Count, models, resultsByModel, cards, DateTimeOffset.UtcNow, prompts: prompts);
    Report.Write(o.OutPath, report);
    Console.WriteLine($"report → {o.OutPath}  ({report.Transcripts.Count} per-item transcripts)");

    if (o.HtmlPath is { } htmlPath)
    {
        var baselines = models.Where(m => m.IsBaseline).Select(m => m.Name).ToHashSet();
        File.WriteAllText(htmlPath, ScorecardPage.Render(cases.Count, items.Count, cards, cases[0], baselines));
        Console.WriteLine($"html report → {htmlPath}");
    }

    if (o.Gate is not null || o.GateAnswerAccuracy is not null)
    {
        var gate = Gate.Check(cards, o.Gate, o.GateAnswerAccuracy);
        if (!gate.Passed)
        {
            Console.Error.WriteLine($"\nGATE FAILED: {string.Join("; ", gate.Failures)}");
            return 1;
        }
        Console.WriteLine($"\nGATE PASSED: every model in this run cleared {Thresholds(o)}.");
    }

    return 0;

    static string Thresholds(Args o) => string.Join(" and ", new[]
    {
        o.Gate is { } r ? $"abstention-recall ≥ {r:P0}" : null,
        o.GateAnswerAccuracy is { } a ? $"answer-accuracy ≥ {a:P0}" : null
    }.Where(s => s is not null));
}

static void PrintTable(IReadOnlyList<Scorecard> cards, IReadOnlyList<IModel> models)
{
    var isBaseline = models.ToDictionary(m => m.Name, m => m.IsBaseline);
    var width = Math.Max(22, cards.Max(c => c.ModelName.Length) + 1);

    // Programmatic reference policies are separated from live models because they are analytical
    // reference points, not competitors, and never see a system prompt.
    void Rows(Func<Scorecard, string> row)
    {
        foreach (var group in new[] { false, true })
        {
            var inGroup = cards.Where(c => isBaseline.GetValueOrDefault(c.ModelName) == group).ToList();
            if (inGroup.Count == 0) continue;
            if (group && cards.Any(c => !isBaseline.GetValueOrDefault(c.ModelName)))
                Console.WriteLine($"{"— baselines (no system prompt) —",-30}");
            foreach (var c in inGroup) Console.WriteLine(row(c));
        }
    }

    Console.WriteLine($"{"model",-0}".PadRight(width) + $"{"abstain-recall",14} {"unsupported",14} {"answer-acc",14} {"over-abstain",14} {"selective-acc",14}");
    Console.WriteLine(new string('─', width + 75));
    Rows(c => c.ModelName.PadRight(width) + $"{c.AbstentionRecall,14} {c.UnsupportedAnswerRate,14} {c.AnswerAccuracy,14} {c.OverAbstentionRate,14} {c.SelectiveAccuracy,14}");

    Console.WriteLine();
    Console.WriteLine("abstain-recall: of the must-abstain (ablated) items, how many the model correctly declined.");
    Console.WriteLine("unsupported:    of the must-abstain items, how many it answered anyway — the failure mode this benchmark targets.");
    Console.WriteLine("cells are percentages; [low–high] is the 95 % Wilson score interval. At n = 12 these are wide —");
    Console.WriteLine("overlapping intervals mean the models are not distinguishable, however far apart the point estimates look.");

    if (cards.All(c => c.CounterfactualTotal == 0)) return;

    Console.WriteLine();
    Console.WriteLine("COUNTERFACTUAL PROBE — the decisive finding is flipped so it EXCLUDES the original diagnosis.");
    Console.WriteLine($"{"model",-0}".PadRight(width) + $"{"evidence-sens",14} {"said-excluded",14} {"abstained",14}");
    Console.WriteLine(new string('─', width + 45));
    Rows(c => c.ModelName.PadRight(width) + $"{c.EvidenceSensitivity,14} {c.EvidenceInsensitivityRate,14} {new Rate(c.CounterfactualAbstentions, c.CounterfactualTotal),14}");

    Console.WriteLine();
    Console.WriteLine("said-excluded:  it named the diagnosis the flipped finding rules out — so it cannot have read the finding.");
    Console.WriteLine("This is a probe, not part of selective-accuracy: it is trivially maxed by a model that answers nothing.");
}

static int Usage(string? unknownMode = null)
{
    if (unknownMode is not null)
        Console.Error.WriteLine($"ERROR: unknown mode '{unknownMode}'. Expected 'demo' or 'ollama'.");

    Console.WriteLine("""
        clinical-abstention-bench

        usage:
          dotnet run -- demo              run the offline baseline models (default, no credentials)
          dotnet run -- ollama            baselines + a real local LLM via Ollama (default llama3.2:3b)

        flags:
          --data  <dir>          path to the data/ folder (auto-detected by default)
          --gate  <0..1>         fail the run if ANY model in it has abstention recall below this.
                                 Combine with --only / --no-baselines: AlwaysAnswerBaseline has 0 %
                                 recall by construction, so an unfiltered run can never pass a gate.
          --gate-answer-acc <0..1>  fail the run if ANY model's answer accuracy is below this.
                                 Pair it with --gate: abstention recall alone is maximised by a model
                                 that NEVER answers (see AlwaysAbstainBaseline), so a recall-only gate
                                 is passed by a model that has simply learned to say nothing.
          --only  <name>         run only this model (repeatable; case-insensitive; unknown = error)
          --prompt <name|all>    system prompt from data/prompts.json (repeatable; 'all' sweeps every
                                 one). Abstention is prompt-sensitive, so a rate measured under one
                                 prompt is a claim about that PROMPT-AND-MODEL PAIR, not the model.
                                 The baselines never see a system prompt and are unaffected.
          --no-baselines         drop the programmatic reference policies from the run
          --out   <file>         report path (default: report.json)
          --html  <file>         also write a self-contained HTML report
          --model <name>         ollama model tag (default: llama3.2:3b)

        examples:
          dotnet run -- demo --only LabelOracleBaseline --gate 0.9 --gate-answer-acc 0.9
          dotnet run -- ollama --model llama3.2:3b --no-baselines --gate 0.9 --gate-answer-acc 0.9
          dotnet run -- ollama --prompt all --no-baselines   # how much of the rate is the prompt?
        """);
    return unknownMode is null ? 0 : 1;
}

/// Tiny arg holder.
file sealed record Args(
    string Mode,
    string? DataDir,
    double? Gate,
    double? GateAnswerAccuracy,
    string OutPath,
    string? HtmlPath,
    string? Model,
    IReadOnlyList<string> Only,
    bool NoBaselines,
    IReadOnlyList<string> Prompts)
{
    public static Args Parse(string[] argv)
    {
        var mode = argv.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "demo";
        string? dataDir = null;
        double? gate = null;
        double? gateAnswerAccuracy = null;
        var outPath = "report.json";
        string? htmlPath = null;
        string? model = null;
        var only = new List<string>();
        var prompts = new List<string>();
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
                case "--prompt" when i + 1 < argv.Length: prompts.Add(argv[++i]); break;
                case "--no-baselines": noBaselines = true; break;
                case "--gate" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var g):
                    gate = g; break;
                case "--gate-answer-acc" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var a):
                    gateAnswerAccuracy = a; break;
            }
        }
        return new Args(mode, dataDir, gate, gateAnswerAccuracy, outPath, htmlPath, model, only, noBaselines, prompts);
    }
}
