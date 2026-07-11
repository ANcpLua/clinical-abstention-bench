using System.Text.Json;

namespace ClinicalAbstentionBench;

/// A complete inference contract. A profile owns both messages so a prompt arm cannot change the
/// system instruction while accidentally retaining a contradictory question in the user message.
public sealed record PromptProfile(
    string Name,
    bool Canonical,
    string Description,
    string SystemText,
    string UserTemplate)
{
    public const string VignetteToken = "{{vignette}}";

    /// Render the exact user message sent to a model. Case data stores only the vignette; the
    /// profile supplies the question, including any deliberate forced-choice language.
    public string RenderUserPrompt(string vignette)
    {
        if (string.IsNullOrWhiteSpace(vignette))
            throw new InvalidDataException($"Cannot render prompt profile '{Name}' with an empty vignette.");

        var firstToken = UserTemplate.IndexOf(VignetteToken, StringComparison.Ordinal);
        if (firstToken < 0 || firstToken != UserTemplate.LastIndexOf(VignetteToken, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Prompt profile '{Name}' must contain {VignetteToken} exactly once in userTemplate.");

        return UserTemplate.Replace(VignetteToken, vignette.Trim(), StringComparison.Ordinal);
    }
}

/// The on-disk shape of data/prompts.json.
public sealed record PromptProfileFile(string Default, List<PromptProfile> Prompts);

/// Implemented by a live adapter whose selected profile controls its system and user messages.
public interface IProfiledModel
{
    PromptProfile PromptProfile { get; }
}

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
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

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
        if (file.SchemaVersion != 2)
            throw new InvalidDataException(
                $"{path} has case schema {file.SchemaVersion}; this runner requires schema 2.");
        if (file.Cases.Count == 0)
            throw new InvalidDataException($"{path} contains zero cases.");
        if (file.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != file.Cases.Count)
            throw new InvalidDataException($"{path} contains duplicate case ids.");
        foreach (var benchmarkCase in file.Cases)
        {
            if (string.IsNullOrWhiteSpace(benchmarkCase.Id)
                || string.IsNullOrWhiteSpace(benchmarkCase.Adjudication))
                throw new InvalidDataException($"{path} contains a case with missing id or adjudication.");
            if (Item.FromCase(benchmarkCase).Any(item => string.IsNullOrWhiteSpace(item.Vignette)))
                throw new InvalidDataException($"Case '{benchmarkCase.Id}' contains an empty vignette.");
        }
        return file.Cases;
    }

    public static List<DiagnosticConcept> LoadConcepts(string dataDir)
    {
        var path = Path.Combine(dataDir, "concepts.json");
        var file = JsonSerializer.Deserialize<ConceptFile>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"{path} did not deserialize.");
        if (file.SchemaVersion != 1)
            throw new InvalidDataException(
                $"{path} has concept schema {file.SchemaVersion}; this runner requires schema 1.");
        if (file.Concepts.Count == 0)
            throw new InvalidDataException($"{path} contains zero diagnostic concepts.");
        return file.Concepts;
    }

    public static IReadOnlyList<Item> ItemsFor(IEnumerable<BenchCase> cases)
        => cases.SelectMany(Item.FromCase).ToList();

    /// Load complete prompt profiles from data/prompts.json and fail closed on an ambiguous
    /// canonical contract or malformed user-message template.
    public static PromptProfileFile LoadPromptProfiles(string dataDir)
    {
        var path = Path.Combine(dataDir, "prompts.json");
        var file = JsonSerializer.Deserialize<PromptProfileFile>(File.ReadAllText(path), Json)
                   ?? throw new InvalidDataException($"{path} did not deserialize.");
        if (file.Prompts.Count == 0)
            throw new InvalidDataException($"{path} defined no prompt profiles.");
        if (file.Prompts.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != file.Prompts.Count)
            throw new InvalidDataException($"{path} contains duplicate prompt-profile names.");

        var defaultProfile = file.Prompts.SingleOrDefault(
            p => string.Equals(p.Name, file.Default, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"{path} names '{file.Default}' as the default, but defines no such prompt profile.");
        var canonical = file.Prompts.Where(p => p.Canonical).ToList();
        if (canonical.Count != 1 || canonical[0] != defaultProfile)
            throw new InvalidDataException(
                $"{path} must define exactly one canonical profile and select it as the default.");

        foreach (var profile in file.Prompts)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)
                || string.IsNullOrWhiteSpace(profile.Description)
                || string.IsNullOrWhiteSpace(profile.SystemText)
                || string.IsNullOrWhiteSpace(profile.UserTemplate))
                throw new InvalidDataException($"Prompt profile '{profile.Name}' has an empty required field.");

            // Render once at load time to validate that the template contains exactly one token.
            _ = profile.RenderUserPrompt("validation vignette");
        }

        return file;
    }

    /// Resolve `--prompt` selections. No selection means the canonical default; "all" sweeps every
    /// complete prompt profile. Fail closed on an unknown name.
    public static List<PromptProfile> SelectPromptProfiles(
        PromptProfileFile file, IReadOnlyCollection<string> requested)
    {
        if (requested.Count == 0)
            return [file.Prompts.Single(p => string.Equals(p.Name, file.Default, StringComparison.OrdinalIgnoreCase))];

        if (requested.Any(r => string.Equals(r, "all", StringComparison.OrdinalIgnoreCase)))
            return [.. file.Prompts];

        var selected = new List<PromptProfile>(requested.Count);
        foreach (var name in requested)
        {
            var match = file.Prompts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"--prompt '{name}' matches no prompt profile in data/prompts.json. Available: {string.Join(", ", file.Prompts.Select(p => p.Name))}, or 'all'.");
            if (!selected.Contains(match))
                selected.Add(match);
        }
        return selected;
    }

    /// Construct the three analytical policies from explicit rules over the case inventory. Policies
    /// that need labels declare that access in provenance; no second response dataset is maintained.
    public static List<IModel> CreateReferenceModels(IReadOnlyList<BenchCase> cases)
        => Enum.GetValues<ReferencePolicy>()
            .Select(policy => (IModel)new ReferencePolicyModel(policy, cases))
            .ToList();

    /// Narrow a run to the models named by `--only` (matched case-insensitively on IModel.Name).
    /// An empty selection means "run everything". Fail-closed: a name that matches no model is an
    /// ERROR, never a silent no-op — a typo must not quietly turn a gated CI run into a green one.
    public static List<IModel> SelectModels(IReadOnlyList<IModel> available, IReadOnlyCollection<string> only)
    {
        if (only.Count == 0)
            return [.. available];

        var selected = new List<IModel>(only.Count);
        foreach (var name in only)
        {
            // A live model's name carries its profile ("llama3.2:3b @ evidence-required"), so bare
            // "llama3.2:3b" selects every prompt variant of it — which is what someone sweeping means.
            var matches = available
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
                            || m.Name.StartsWith(name + " @ ", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                throw new InvalidOperationException(
                    $"--only '{name}' matched no model in this run. Available: {string.Join(", ", available.Select(m => m.Name))}.");

            foreach (var match in matches)
                if (!selected.Contains(match))
                    selected.Add(match);
        }
        return selected;
    }

    /// Run every item through one model, in item order. Live adapters render their selected profile;
    /// reference policies receive the canonical user message but deliberately receive no system
    /// prompt. ItemResult retains the rendered message separately, so the transcript records
    /// byte-for-byte what was sent rather than only the vignette stored in cases.json.
    public static async Task<List<ItemResult>> RunModelAsync(
        IModel model,
        IReadOnlyList<Item> items,
        PromptProfile canonicalProfile,
        IGrader grader,
        CancellationToken ct = default)
    {
        var profile = model is IProfiledModel profiled ? profiled.PromptProfile : canonicalProfile;

        var results = new List<ItemResult>(items.Count);
        foreach (var item in items)
        {
            var renderedPrompt = profile.RenderUserPrompt(item.Vignette);
            var response = await model.AnswerAsync(new ModelInput(item.Key, renderedPrompt), ct);
            var grade = grader.Score(item, response);
            results.Add(new ItemResult(
                model.Name,
                item,
                model.SystemPrompt,
                renderedPrompt,
                response,
                grade));
        }
        return results;
    }
}
