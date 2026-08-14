using System.ComponentModel.DataAnnotations;

namespace ChangeLens.Application.Dtos;

public sealed class CreateRepositoryRequest
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DefaultBranch { get; set; }

    [Required, MaxLength(50)]
    public string Language { get; set; } = string.Empty;
}

public sealed class RepositoryResponse
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string? DefaultBranch { get; init; }

    public string Language { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}
