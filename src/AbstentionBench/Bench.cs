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

    /// Run every item through one model, in item order.
    public static async Task<List<ItemResult>> RunModelAsync(
        IModel model, IReadOnlyList<Item> items, CancellationToken ct = default)
    {
        var results = new List<ItemResult>(items.Count);
        foreach (var item in items)
        {
            var response = await model.AnswerAsync(item, ct);
            results.Add(new ItemResult(model.Name, item, response, Scoring.Score(item, response)));
        }
        return results;
    }
}
