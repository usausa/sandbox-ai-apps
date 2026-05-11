namespace Flyer.Services.Models;

public sealed record ProductSearchResult(
    string Id,
    string Name,
    int Price,
    string? Category,
    double Score);
