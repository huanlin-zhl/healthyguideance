namespace HealthyGuidance.Domain.Models;

public sealed record Menu(
    string RawText,
    IReadOnlyList<string> Items);
