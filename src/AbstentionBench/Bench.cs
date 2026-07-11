using System.Text.Json;

namespace ClinicalAbstentionBench;

/// Loading + running logic, kept separate from the CLI so it is unit-testable.
public static class Bench
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// Walk upward from the current directory and the app base directory until a
    /// folder containing data/cases.json is found. Overridable with --data.
    public static string FindDataDir(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "cases.json")))
                    return Path.Combine(dir.FullName, "data");
            }
        }
        throw new FileNotFoundException(
            "Could not locate data/cases.json (searched upward from cwd and app base). Pass --data <dir>.");
    }

    public static List<BenchCase> LoadCases(string dataDir)
    {
        var path = Path.Combine(dataDir, "cases.json");
        var file = JsonSerializer.Deserialize<CaseFile>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"{path} did not deserialize.");
        if (file.Cases.Count == 0)
            throw new InvalidDataException($"{path} contains zero cases.");
        return file.Cases;
    }

    public static IReadOnlyList<Item> ItemsFor(IEnumerable<BenchCase> cases)
        => cases.SelectMany(Item.FromCase).ToList();

    /// Load the deterministic demo models from data/demo-responses.json.
    public static List<IModel> LoadDemoModels(string dataDir)
    {
        var path = Path.Combine(dataDir, "demo-responses.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var models = new List<IModel>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.StartsWith('_')) continue; // skip _note
            var replies = new Dictionary<string, string>();
            foreach (var reply in prop.Value.EnumerateObject())
                replies[reply.Name] = reply.Value.GetString() ?? "";
            models.Add(new ScriptedModel(prop.Name, replies));
        }
        if (models.Count == 0)
            throw new InvalidDataException($"{path} defined no models.");
        return models;
    }

    /// Narrow a run to the models named by `--only` (matched case-insensitively on IModel.Name).
    /// An empty selection means "run everything". Fail-closed: a name that matches no model is an
    /// ERROR, never a silent no-op — a typo must not quietly turn a gated CI run into a green one.
    public static List<IModel> SelectModels(IReadOnlyList<IModel> available, IReadOnlyCollection<string> only)
    {
        if (only.Count == 0) return [.. available];

        var selected = new List<IModel>(only.Count);
        foreach (var name in only)
        {
            var match = available.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"--only '{name}' matched no model in this run. Available: {string.Join(", ", available.Select(m => m.Name))}.");
            if (!selected.Contains(match)) selected.Add(match);
        }
        return selected;
    }

    /// Run every item through one model, in item order, scoring each reply with `grader`.
    public static async Task<List<ItemResult>> RunModelAsync(
        IModel model, IReadOnlyList<Item> items, IGrader? grader = null, CancellationToken ct = default)
    {
        grader ??= LexicalGrader.Instance;

        var results = new List<ItemResult>(items.Count);
        foreach (var item in items)
        {
            var response = await model.AnswerAsync(item, ct);
            results.Add(new ItemResult(model.Name, item, model.SystemPrompt, response, grader.Score(item, response)));
        }
        return results;
    }
}
