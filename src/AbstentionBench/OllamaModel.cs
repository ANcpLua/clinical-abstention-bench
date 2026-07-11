using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClinicalAbstentionBench;

/// A real local LLM served by Ollama (https://ollama.com), queried via /api/chat at temperature 0.
///
/// A complete prompt profile is passed in rather than hardcoded. The profile owns both the system
/// and user messages; its name is part of the model identity because a score belongs to that pair.
public sealed class OllamaModel : IModel, IProfiledModel
{
    private const double Temperature = 0;

    private readonly string _model;
    private readonly string _baseUrl;
    private readonly PromptProfile _profile;
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _provenance;
    private bool _preflighted;

    public OllamaModel(
        string model,
        PromptProfile profile,
        string baseUrl = "http://localhost:11434",
        HttpClient? http = null)
    {
        _model = model;
        _profile = profile;
        _baseUrl = baseUrl;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _provenance = new Dictionary<string, string>
        {
            ["kind"] = "ollama",
            ["baseUrl"] = baseUrl,
            ["modelTag"] = model,
            ["promptName"] = profile.Name,
            ["promptCanonical"] = profile.Canonical.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            ["systemPrompt"] = profile.SystemText,
            ["userTemplate"] = profile.UserTemplate,
            ["responseFormat"] = "json",
            ["temperature"] = Temperature.ToString("0.###", CultureInfo.InvariantCulture)
        };
    }

    /// e.g. "llama3.2:3b @ evidence-required". The profile is in the name on purpose.
    public string Name => $"{_model} @ {_profile.Name}";

    public bool IsBaseline => false;

    public PromptProfile PromptProfile => _profile;

    public string? SystemPrompt => _profile.SystemText;

    public IReadOnlyDictionary<string, string> Provenance => _provenance;

    /// Confirm the model tag exists on the server and record its digest, so the report says exactly
    /// which weights produced the numbers. Fail-closed: a requested-but-absent model is an ERROR.
    public async Task PreflightAsync(CancellationToken ct = default)
    {
        if (_preflighted)
            return;

        using var response = await _http.GetAsync($"{_baseUrl}/api/tags", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama is not reachable at {_baseUrl} ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var installed = doc.RootElement.GetProperty("models").EnumerateArray().ToList();
        var match = installed.FirstOrDefault(m =>
            m.TryGetProperty("model", out var tag) &&
            string.Equals(tag.GetString(), _model, StringComparison.OrdinalIgnoreCase));

        if (match.ValueKind != JsonValueKind.Object)
        {
            var names = installed
                .Select(m => m.TryGetProperty("model", out var t) ? t.GetString() : null)
                .Where(n => n is not null);
            throw new InvalidOperationException(
                $"Ollama model '{_model}' is not installed at {_baseUrl}. Available: {string.Join(", ", names)}. Run `ollama pull {_model}`.");
        }

        if (match.TryGetProperty("digest", out var digest) && digest.GetString() is { } sha)
            _provenance["modelDigest"] = $"sha256:{sha}";
        if (match.TryGetProperty("details", out var details))
        {
            if (details.TryGetProperty("parameter_size", out var ps) && ps.GetString() is { } size)
                _provenance["parameterSize"] = size;
            if (details.TryGetProperty("quantization_level", out var q) && q.GetString() is { } quant)
                _provenance["quantization"] = quant;
        }

        _preflighted = true;
    }

    public async Task<string> AnswerAsync(ModelInput input, CancellationToken ct = default)
    {
        await PreflightAsync(ct);

        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            stream = false,
            format = "json",
            options = new { temperature = Temperature },
            messages = new object[]
            {
                new { role = "system", content = _profile.SystemText },
                new { role = "user", content = input.Prompt }
            }
        });

        using var response = await _http.PostAsync(
            $"{_baseUrl}/api/chat",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama {(int)response.StatusCode} for model '{_model}': {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidDataException($"Ollama reply for '{_model}' had no message.content.");
    }
}
