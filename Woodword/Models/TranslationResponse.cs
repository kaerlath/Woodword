using System.Text.Json.Serialization;

namespace Woodword.Models;

public sealed record TranslationResponse
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("direction")] public string? Direction { get; init; }
    [JsonPropertyName("sourceLanguage")] public string? SourceLanguage { get; init; }
    [JsonPropertyName("targetLanguage")] public string? TargetLanguage { get; init; }
    [JsonPropertyName("translation")] public string? Translation { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
