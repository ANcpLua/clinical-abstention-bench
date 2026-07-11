using System.Text.Json;
using ClinicalAbstentionBench;
using Xunit;

namespace AbstentionBench.Tests;

/// The tracked result files are recorded model observations plus derived scores. Replaying every raw
/// response through the current repository dataset prevents a grader or label change from leaving
/// stale outcomes and aggregates behind.
public class ResultArtifactTests
{
    [Theory]
    [InlineData("llama3.2-3b.json")]
    [InlineData("llama3.2-3b-prompt-sweep.json")]
    public void RecordedResponses_ReproduceEveryOutcomeAndAggregate(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(RepositoryBenchmark.DataDirectory, "..", "results", fileName));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var items = RepositoryBenchmark.Items.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var resultsByModel = new Dictionary<string, List<ItemResult>>(StringComparer.Ordinal);

        foreach (var transcript in root.GetProperty("transcripts").EnumerateArray())
        {
            var key = transcript.GetProperty("itemKey").GetString()!;
            var item = items[key];
            var model = transcript.GetProperty("model").GetString()!;
            var response = transcript.GetProperty("response").GetString()!;
            var systemPrompt = transcript.TryGetProperty("systemPrompt", out var promptElement)
                ? promptElement.GetString()
                : null;
            var outcome = LexicalGrader.Instance.Score(item, response);

            Assert.Equal(item.Prompt, transcript.GetProperty("prompt").GetString());
            Assert.Equal(item.GroundTruth, transcript.GetProperty("supportedAnswer").GetString());
            Assert.Equal(outcome.ToString(), transcript.GetProperty("outcome").GetString());

            if (!resultsByModel.TryGetValue(model, out var modelResults))
            {
                modelResults = [];
                resultsByModel.Add(model, modelResults);
            }
            modelResults.Add(new ItemResult(model, item, systemPrompt, response, outcome));
        }

        var reportedModels = root.GetProperty("models").EnumerateArray().ToList();
        Assert.Equal(
            reportedModels.Select(model => model.GetProperty("modelName").GetString()).Order(),
            resultsByModel.Keys.Order());

        foreach (var reported in reportedModels)
        {
            var modelName = reported.GetProperty("modelName").GetString()!;
            var card = Scorecard.From(modelName, resultsByModel[modelName]);

            AssertRate(reported, "abstentionRecall", card.AbstentionRecall);
            AssertRate(reported, "unsupportedAnswerRate", card.UnsupportedAnswerRate);
            AssertRate(reported, "answerAccuracy", card.AnswerAccuracy);
            AssertRate(reported, "overAbstentionRate", card.OverAbstentionRate);
            AssertRate(reported, "selectiveAccuracy", card.SelectiveAccuracy);
            AssertRate(reported, "evidenceSensitivity", card.EvidenceSensitivity);
            AssertRate(reported, "evidenceInsensitivityRate", card.EvidenceInsensitivityRate);

            Assert.Equal(card.AblatedTotal, reported.GetProperty("ablatedTotal").GetInt32());
            Assert.Equal(card.CorrectAbstentions, reported.GetProperty("correctAbstentions").GetInt32());
            Assert.Equal(card.UnsupportedAnswers, reported.GetProperty("unsupportedAnswers").GetInt32());
            Assert.Equal(card.FullTotal, reported.GetProperty("fullTotal").GetInt32());
            Assert.Equal(card.CorrectAnswers, reported.GetProperty("correctAnswers").GetInt32());
            Assert.Equal(card.WrongAnswers, reported.GetProperty("wrongAnswers").GetInt32());
            Assert.Equal(card.OverAbstentions, reported.GetProperty("overAbstentions").GetInt32());
            Assert.Equal(card.CounterfactualTotal, reported.GetProperty("counterfactualTotal").GetInt32());
            Assert.Equal(card.CounterfactualAbstentions, reported.GetProperty("counterfactualAbstentions").GetInt32());
            Assert.Equal(card.EvidenceInsensitiveAnswers, reported.GetProperty("evidenceInsensitiveAnswers").GetInt32());
            Assert.Equal(card.CounterfactualOtherAnswers, reported.GetProperty("counterfactualOtherAnswers").GetInt32());
        }
    }

    private static void AssertRate(JsonElement model, string propertyName, Rate expected)
    {
        var actual = model.GetProperty(propertyName);
        Assert.Equal(expected.Successes, actual.GetProperty("successes").GetInt32());
        Assert.Equal(expected.Total, actual.GetProperty("total").GetInt32());
        Assert.Equal(expected.Value, actual.GetProperty("value").GetDouble());
        Assert.Equal(expected.Lower, actual.GetProperty("ci95")[0].GetDouble());
        Assert.Equal(expected.Upper, actual.GetProperty("ci95")[1].GetDouble());
    }
}
