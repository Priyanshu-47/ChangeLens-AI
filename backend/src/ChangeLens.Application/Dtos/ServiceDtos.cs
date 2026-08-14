using System.ComponentModel.DataAnnotations;

namespace ChangeLens.Application.Dtos;

public sealed class CreateServiceRequest
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(500)]
    public string? RootPath { get; set; }
}

public sealed class ServiceResponse
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Language { get; init; }

    public string? RootPath { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
