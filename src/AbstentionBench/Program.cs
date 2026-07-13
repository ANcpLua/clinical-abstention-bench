using System.Globalization;
using ClinicalAbstentionBench;

// clinical-abstention-bench — calibrated evidence assessment on clinical vignettes.
//
// Each case is shown in three evidence states: full, ablated, and a contrast that supports an
// explicit alternative. The target for each state independently specifies diagnosis, certainty,
// and urgency; arm names never imply that silence is the right answer.
//
// Fail-closed, mirroring the ancplua.evaluation engine: the run exits 0 only when every item
// was scored and the report was written. A requested-but-unavailable model is an ERROR (exit 1),
// never a silent skip. Optional gates enforce coverage, conditional diagnostic accuracy, and
// urgency accuracy on every selected model; use --only / --no-baselines to select the model at hand.

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
    var grader = new StructuredConceptGrader(new ConceptCatalog(Bench.LoadConcepts(dataDir)));
    var items = Bench.ItemsFor(cases);
    var profileFile = Bench.LoadPromptProfiles(dataDir);
    var selectedProfiles = Bench.SelectPromptProfiles(profileFile, o.Prompts);
    var canonicalProfile = profileFile.Prompts.Single(p => p.Canonical);

    var available = new List<IModel>();
    if (!o.NoBaselines)
        available.AddRange(Bench.CreateReferenceModels(cases));

    // One run per (model, prompt profile). Each profile owns both the system and user messages.
    if (live)
        available.AddRange(selectedProfiles.Select(p => new OllamaModel(o.Model ?? "llama3.2:3b", p)));

    var models = Bench.SelectModels(available, o.Only);
    if (models.Count == 0)
        throw new InvalidOperationException(
            "No models selected. --no-baselines removed every model from a 'demo' run — use 'ollama' to add a live model, or drop the flag.");

    var promptNote = live && selectedProfiles.Count > 1 ? $" · {selectedProfiles.Count} prompt profiles" : "";
    Console.WriteLine($"clinical-abstention-bench · {cases.Count} cases → {items.Count} items · {models.Count} models{promptNote}\n");

    var cards = new List<Scorecard>();
    var resultsByModel = new Dictionary<string, IReadOnlyList<ItemResult>>();
    foreach (var model in models)
    {
        // Reference policies use the canonical rendered user message for auditable transcripts but
        // still receive no system message. A live adapter renders its own selected profile.
        var results = await Bench.RunModelAsync(model, items, canonicalProfile, grader);
        resultsByModel[model.Name] = results;
        cards.Add(Scorecard.From(model.Name, results));
    }

    PrintTable(cards, models);

    var profilesInRun = live ? selectedProfiles : [canonicalProfile];
    var report = Report.Build(
        o.Mode,
        cases.Count,
        items.Count,
        models,
        resultsByModel,
        cards,
        DateTimeOffset.UtcNow,
        grader: grader,
        prompts: profilesInRun);
    Report.Write(o.OutPath, report);
    Console.WriteLine($"report → {o.OutPath}  ({report.Transcripts.Count} per-item transcripts)");

    if (o.GateCoverage is not null || o.GateSelectiveAccuracy is not null || o.GateUrgencyAccuracy is not null)
    {
        var gate = Gate.Check(cards, o.GateCoverage, o.GateSelectiveAccuracy, o.GateUrgencyAccuracy);
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
        o.GateCoverage is { } c ? $"coverage ≥ {c:P0}" : null,
        o.GateSelectiveAccuracy is { } a ? $"selective-accuracy ≥ {a:P0}" : null,
        o.GateUrgencyAccuracy is { } u ? $"urgency-accuracy ≥ {u:P0}" : null
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
            if (inGroup.Count == 0)
                continue;
            if (group && cards.Any(c => !isBaseline.GetValueOrDefault(c.ModelName)))
                Console.WriteLine($"{"— baselines (no system prompt) —",-30}");
            foreach (var c in inGroup)
                Console.WriteLine(row(c));
        }
    }

    Console.WriteLine($"{"model",-0}".PadRight(width) + $"{"coverage",14} {"selective-acc",14} {"decision-acc",14} {"certainty-acc",14} {"urgency-acc",14} {"undertriage",14}");
    Console.WriteLine(new string('─', width + 90));
    Rows(c => c.ModelName.PadRight(width) + $"{c.Coverage,14} {c.SelectiveAccuracy,14} {c.DecisionAccuracy,14} {c.CertaintyAccuracy,14} {c.UrgencyAccuracy,14} {c.UndertriageRate,14}");

    Console.WriteLine();
    Console.WriteLine("coverage:       fraction of full + ablated items on which the model supplied a diagnosis.");
    Console.WriteLine("selective-acc:  diagnostic accuracy conditional on supplying a diagnosis.");
    Console.WriteLine("decision-acc:   correct diagnosis or correct diagnostic deferral on full + ablated items.");
    Console.WriteLine("undertriage:    urgency lower than the evidence-state target, scored independently of diagnosis.");
    Console.WriteLine("cells are percentages; [low–high] is the 95 % Wilson score interval. With this small set they are wide —");
    Console.WriteLine("overlapping intervals mean the models are not distinguishable, however far apart the point estimates look.");

    if (cards.All(c => c.ContrastTotal == 0))
        return;

    Console.WriteLine();
    Console.WriteLine("CONTRAST ARM — changed evidence supports an explicit alternative target.");
    Console.WriteLine($"{"model",-0}".PadRight(width) + $"{"contrast-acc",14} {"orig-persists",14} {"paired-revise",14} {"certainty-acc",14} {"urgency-acc",14} {"undertriage",14}");
    Console.WriteLine(new string('─', width + 90));
    Rows(c => c.ModelName.PadRight(width) + $"{c.ContrastAccuracy,14} {c.OriginalTargetPersistence,14} {c.PairedRevisionAccuracy,14} {c.ContrastCertaintyAccuracy,14} {c.ContrastUrgencyAccuracy,14} {c.ContrastUndertriageRate,14}");

    Console.WriteLine();
    Console.WriteLine("orig-persists:  response still names the original concept after the evidence supports another target.");
    Console.WriteLine("paired-revise:  correct on both states, using a contrast concept not supported by the full state.");
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
          --gate-coverage <0..1> fail if any selected model answers fewer than this fraction of the
                                 primary full + ablated items.
          --gate-selective-acc <0..1>
                                 fail if conditional diagnostic accuracy among answered primary
                                 items is below this. Pair with coverage so selective accuracy cannot
                                 be inflated by answering only a tiny easy subset.
          --gate-urgency-acc <0..1>
                                 optional independent floor for exact urgency classification.
          --only  <name>         run only this model (repeatable; case-insensitive; unknown = error)
          --prompt <name|all>    complete prompt profile from data/prompts.json (repeatable; 'all'
                                 sweeps every profile). A profile controls both system and user
                                 messages. The canonical default is evidence-required; forced-choice
                                 is a noncanonical stress arm. Reference policies use the canonical
                                 user template but never receive a system message.
          --no-baselines         drop the programmatic reference policies from the run
          --out   <file>         report path (default: report.json)
          --model <name>         ollama model tag (default: llama3.2:3b)

        examples:
          dotnet run -- demo --only LabelOracleBaseline --gate-coverage 0.5 --gate-selective-acc 0.9
          dotnet run -- ollama --model llama3.2:3b --no-baselines --gate-coverage 0.5 --gate-selective-acc 0.9
          dotnet run -- ollama --prompt all --no-baselines   # compare canonical vs forced choice
        """);
    return unknownMode is null ? 0 : 1;
}

/// Tiny arg holder.
file sealed record Args(
    string Mode,
    string? DataDir,
    double? GateCoverage,
    double? GateSelectiveAccuracy,
    double? GateUrgencyAccuracy,
    string OutPath,
    string? Model,
    IReadOnlyList<string> Only,
    bool NoBaselines,
    IReadOnlyList<string> Prompts)
{
    public static Args Parse(string[] argv)
    {
        var mode = argv.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "demo";
        string? dataDir = null;
        double? gateCoverage = null;
        double? gateSelectiveAccuracy = null;
        double? gateUrgencyAccuracy = null;
        var outPath = "report.json";
        string? model = null;
        var only = new List<string>();
        var prompts = new List<string>();
        var noBaselines = false;

        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--data" when i + 1 < argv.Length:
                    dataDir = argv[++i];
                    break;
                case "--out" when i + 1 < argv.Length:
                    outPath = argv[++i];
                    break;
                case "--model" when i + 1 < argv.Length:
                    model = argv[++i];
                    break;
                case "--only" when i + 1 < argv.Length:
                    only.Add(argv[++i]);
                    break;
                case "--prompt" when i + 1 < argv.Length:
                    prompts.Add(argv[++i]);
                    break;
                case "--no-baselines":
                    noBaselines = true;
                    break;
                case "--gate-coverage" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var g):
                    gateCoverage = g;
                    break;
                case "--gate-selective-acc" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var a):
                    gateSelectiveAccuracy = a;
                    break;
                case "--gate-urgency-acc" when i + 1 < argv.Length
                    && double.TryParse(argv[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var u):
                    gateUrgencyAccuracy = u;
                    break;
            }
        }
        return new Args(
            mode,
            dataDir,
            gateCoverage,
            gateSelectiveAccuracy,
            gateUrgencyAccuracy,
            outPath,
            model,
            only,
            noBaselines,
            prompts);
    }
}
