using ClinicalAbstentionBench;

namespace AbstentionBench.Tests;

/// Repository-backed benchmark data shared by tests. This is the same dataset and the same
/// programmatic reference policies used by the application; it deliberately contains no substitute
/// clinical cases, empty model doubles, or copied response tables.
internal static class RepositoryBenchmark
{
    public static readonly string DataDirectory = Bench.FindDataDir();
    public static readonly IReadOnlyList<BenchCase> Cases = Bench.LoadCases(DataDirectory);
    public static readonly IReadOnlyList<DiagnosticConcept> Concepts = Bench.LoadConcepts(DataDirectory);
    public static readonly ConceptCatalog Catalog = new(Concepts);
    public static readonly StructuredConceptGrader Grader = new(Catalog);
    public static readonly PromptProfileFile PromptProfiles = Bench.LoadPromptProfiles(DataDirectory);
    public static readonly PromptProfile CanonicalProfile = PromptProfiles.Prompts.Single(profile => profile.Canonical);
    public static readonly IReadOnlyList<Item> Items = Bench.ItemsFor(Cases);
    public static readonly IReadOnlyList<IModel> ReferenceModels = Bench.CreateReferenceModels(Cases);

    public static BenchCase Case(string id)
        => Cases.Single(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    public static Item Item(string caseId, Variant variant)
        => Items.Single(i => i.CaseId == caseId && i.Variant == variant);

    public static ReferencePolicyModel Policy(ReferencePolicy policy)
        => ReferenceModels.OfType<ReferencePolicyModel>().Single(model => model.Policy == policy);

    public static Dictionary<string, Scorecard> Scorecards()
        => ReferenceModels
            .Select(model =>
            {
                var results = Run(model).GetAwaiter().GetResult();
                return Scorecard.From(model.Name, results);
            })
            .ToDictionary(card => card.ModelName, StringComparer.Ordinal);

    public static Task<List<ItemResult>> Run(IModel model, IReadOnlyList<Item>? items = null)
        => Bench.RunModelAsync(model, items ?? Items, CanonicalProfile, Grader);

    public static ItemResult Grade(
        Item item,
        ModelResponse response,
        string modelName = "test-policy")
    {
        var raw = StructuredResponseJson.Serialize(response);
        return new ItemResult(
            modelName,
            item,
            null,
            CanonicalProfile.RenderUserPrompt(item.Vignette),
            raw,
            Grader.Score(item, raw));
    }

    public static ModelResponse TargetResponse(Item item)
        => new(item.Target.Diagnosis, item.Target.DiagnosticStatus, item.Target.Urgency);
}
