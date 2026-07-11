using ClinicalAbstentionBench;

namespace AbstentionBench.Tests;

/// Repository-backed benchmark data shared by tests. This is the same dataset and the same
/// programmatic reference policies used by the application; it deliberately contains no substitute
/// clinical cases, empty model doubles, or copied response tables.
internal static class RepositoryBenchmark
{
    public static readonly string DataDirectory = Bench.FindDataDir();
    public static readonly IReadOnlyList<BenchCase> Cases = Bench.LoadCases(DataDirectory);
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
                var results = Bench.RunModelAsync(model, Items).GetAwaiter().GetResult();
                return Scorecard.From(model.Name, results);
            })
            .ToDictionary(card => card.ModelName, StringComparer.Ordinal);
}
