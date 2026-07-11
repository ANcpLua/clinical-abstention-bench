using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

public class ScoringTests
{
    [Fact]
    public void FromCase_ProducesOneAnswerableAndTwoMustAbstain()
    {
        foreach (var benchmarkCase in RepositoryBenchmark.Cases)
        {
            var items = Item.FromCase(benchmarkCase).ToList();

            Assert.Collection(
                items,
                full =>
                {
                    Assert.Equal(Variant.Full, full.Variant);
                    Assert.False(full.MustAbstain);
                    Assert.Equal(benchmarkCase.FullPrompt, full.Prompt);
                    Assert.Equal(benchmarkCase.ExpectedAnswer, full.GroundTruth);
                    Assert.Equal($"{benchmarkCase.Id}:full", full.Key);
                },
                ablated =>
                {
                    Assert.Equal(Variant.Ablated, ablated.Variant);
                    Assert.True(ablated.MustAbstain);
                    Assert.Equal(benchmarkCase.AblatedPrompt, ablated.Prompt);
                    Assert.Equal("INSUFFICIENT", ablated.GroundTruth);
                    Assert.Equal($"{benchmarkCase.Id}:ablated", ablated.Key);
                },
                counterfactual =>
                {
                    Assert.Equal(Variant.Counterfactual, counterfactual.Variant);
                    Assert.True(counterfactual.MustAbstain);
                    Assert.Equal(benchmarkCase.CounterfactualPrompt, counterfactual.Prompt);
                    Assert.Equal("INSUFFICIENT", counterfactual.GroundTruth);
                    Assert.Equal($"{benchmarkCase.Id}:counterfactual", counterfactual.Key);
                });
        }
    }

    /// The answerable item carries the case's synonyms; must-abstain items accept only the abstention
    /// sentinel. The counterfactual item separately carries the diagnosis forms it must NOT say.
    [Fact]
    public void FromCase_AcceptedFormsCoverTheCanonicalAnswerAndItsSynonyms()
    {
        foreach (var benchmarkCase in RepositoryBenchmark.Cases)
        {
            var items = Item.FromCase(benchmarkCase).ToList();
            var expectedAccepted = new[] { benchmarkCase.ExpectedAnswer }
                .Concat(benchmarkCase.AcceptedAnswers ?? [])
                .ToList();
            var expectedExcluded = new[] { benchmarkCase.ExpectedAnswer }
                .Concat(benchmarkCase.CounterfactualExcludedAnswers ?? benchmarkCase.AcceptedAnswers ?? [])
                .ToList();

            var full = items.Single(i => i.Variant == Variant.Full);
            Assert.Equal(expectedAccepted, full.AcceptedForms);
            Assert.Empty(full.ExcludedForms);

            var ablated = items.Single(i => i.Variant == Variant.Ablated);
            Assert.Equal([ablated.GroundTruth], ablated.AcceptedForms);
            Assert.Empty(ablated.ExcludedForms);

            // The counterfactual item's job is to know what the flipped evidence now rules OUT.
            var counterfactual = items.Single(i => i.Variant == Variant.Counterfactual);
            Assert.Equal([counterfactual.GroundTruth], counterfactual.AcceptedForms);
            Assert.Equal(expectedExcluded, counterfactual.ExcludedForms);
        }
    }

    [Fact]
    public async Task Scorecard_AggregatesRatesCorrectly()
    {
        const string scenario = nameof(Scorecard_AggregatesRatesCorrectly);
        var grader = LexicalGrader.Instance;
        var full = RepositoryBenchmark.Items.Where(i => i.Variant == Variant.Full).Take(2).ToList();
        var ablated = RepositoryBenchmark.Items.Where(i => i.Variant == Variant.Ablated).Skip(2).Take(2).ToList();
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);

        ItemResult Result(Item item, string response)
            => new(scenario, item, null, response, grader.Score(item, response));

        var wrongResponse = RepositoryBenchmark.Items
            .Where(i => i.Variant == Variant.Full && i.CaseId != full[1].CaseId)
            .Select(i => i.GroundTruth)
            .First(response => grader.Score(full[1], response) == Outcome.WrongAnswer);
        var correctFullResponse = await labelOracle.AnswerAsync(new ModelInput(full[0].Key, full[0].Prompt));
        var correctAbstention = await labelOracle.AnswerAsync(new ModelInput(ablated[0].Key, ablated[0].Prompt));
        var unsupportedAnswer = await alwaysAnswer.AnswerAsync(new ModelInput(ablated[1].Key, ablated[1].Prompt));

        var results = new List<ItemResult>
        {
            Result(full[0], correctFullResponse),
            Result(full[1], wrongResponse),
            Result(ablated[0], correctAbstention),
            Result(ablated[1], unsupportedAnswer)
        };

        Assert.Equal(
            [Outcome.CorrectAnswer, Outcome.WrongAnswer, Outcome.CorrectAbstention, Outcome.UnsupportedAnswer],
            results.Select(result => result.Outcome));

        var card = Scorecard.From(scenario, results);

        Assert.Equal(2, card.FullTotal);
        Assert.Equal(2, card.AblatedTotal);
        Assert.Equal(0.5, card.AnswerAccuracy);
        Assert.Equal(0.5, card.AbstentionRecall);
        Assert.Equal(0.5, card.UnsupportedAnswerRate);
        Assert.Equal(0.5, card.SelectiveAccuracy); // 2 of 4 items matched what the evidence supported
    }
}

public class DatasetIntegrationTests
{
    [Fact]
    public void ReferencePolicies_BehaveAsDeclared_OnRepositoryDataset()
    {
        Assert.NotEmpty(RepositoryBenchmark.Cases);
        Assert.Equal(
            RepositoryBenchmark.Cases.Count * Enum.GetValues<Variant>().Length,
            RepositoryBenchmark.Items.Count);

        var cards = RepositoryBenchmark.Scorecards();
        var alwaysAnswer = RepositoryBenchmark.Policy(ReferencePolicy.AlwaysAnswer);
        var labelOracle = RepositoryBenchmark.Policy(ReferencePolicy.LabelOracle);

        // The always-answer policy never abstains -> every ablated item is an unsupported answer.
        Assert.Equal(1.0, cards[alwaysAnswer.Name].UnsupportedAnswerRate);
        Assert.Equal(0.0, cards[alwaysAnswer.Name].AbstentionRecall);

        // The label oracle abstains on every ablated item and answers the full arm by construction.
        Assert.Equal(1.0, cards[labelOracle.Name].AbstentionRecall);
        Assert.Equal(1.0, cards[labelOracle.Name].AnswerAccuracy);

        // The whole point: the benchmark separates them.
        Assert.True(cards[labelOracle.Name].SelectiveAccuracy > cards[alwaysAnswer.Name].SelectiveAccuracy);
    }
}
