namespace Woodword.Models;

public sealed class HistoryEntry
{
    public DateTimeOffset TimestampUtc { get; init; }
    public TranslationDirection Direction { get; init; }
    public string Input { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
}

public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, long? OlderEntriesBeforeOffset);
