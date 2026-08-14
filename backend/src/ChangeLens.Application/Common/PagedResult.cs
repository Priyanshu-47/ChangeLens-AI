namespace ChangeLens.Application.Common;

/// <summary>Stable pagination envelope used by all list endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
