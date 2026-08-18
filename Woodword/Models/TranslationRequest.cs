using System.Text.Json.Serialization;

namespace Woodword.Models;

public sealed record TranslationRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("direction")] string Direction);
