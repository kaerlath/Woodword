namespace Woodword.Models;

public sealed record LiveTranslationEntry(
    DateTime Timestamp,
    string Channel,
    string Sender,
    string VieranText,
    string CommonText);
