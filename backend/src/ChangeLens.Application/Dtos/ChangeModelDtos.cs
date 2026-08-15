namespace ChangeLens.Application.Dtos;

/// <summary>
/// One normalized symbol from the Roslyn analyzer (docs/rag-architecture.md §8).
/// The SymbolId is stable and becomes the evidence id `symbol:<id>` in the AI request.
/// </summary>
public sealed class ChangedSymbolDto
{
    public string SymbolId { get; init; } = string.Empty;

    /// <summary>Class / Interface / Method / Constructor / Property / Field / Struct / Enum.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string FullyQualifiedName { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public string? Namespace { get; init; }

    public string? Project { get; init; }

    public string? Signature { get; init; }

    public string? ReturnType { get; init; }

    public List<string> Parameters { get; init; } = [];
}

/// <summary>One Roslyn-proven dependency edge (docs/rag-architecture.md §9).</summary>
public sealed class DependencyEdgeDto
{
    public string FromSymbolId { get; init; } = string.Empty;

    public string ToSymbolId { get; init; } = string.Empty;

    /// <summary>CALLS / REFERENCES_TYPE / IMPLEMENTS / INHERITS.</summary>
    public string EdgeType { get; init; } = string.Empty;

    public string? FilePath { get; init; }
}

/// <summary>A potentially affected API endpoint (brief §17).</summary>
public sealed class ApiEndpointDto
{
    public string Controller { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string? RequestDto { get; init; }

    public string? ResponseDto { get; init; }

    public string FilePath { get; init; } = string.Empty;
}

/// <summary>An external integration (HttpClient-based) connected to the change (brief §18).</summary>
public sealed class ExternalIntegrationImpactDto
{
    public string ClientType { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public List<string> EndpointHints { get; init; } = [];

    public string? RetryHint { get; init; }

    public string? TimeoutHint { get; init; }

    public List<string> ConnectedChangedSymbols { get; init; } = [];
}

/// <summary>
/// The change-intelligence model the backend derives with Roslyn and hands to the AI
/// service (brief §23): changed/impacted symbols, dependency relationships, impacted
/// APIs, and external integrations. Evidence is discovered server-side — the client
/// does not supply it.
/// </summary>
/// <summary>One resolved dependency path between two repo symbols (Phase 8 tool).</summary>
public sealed class SymbolDependencyPathDto
{
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    /// <summary>CALLS | REFERENCES_TYPE | IMPLEMENTS | INHERITS</summary>
    public string EdgeType { get; set; } = string.Empty;

    public string? FilePath { get; set; }
}

/// <summary>Result of the get_dependency_paths tool (bounded traversal, read-only).</summary>
public sealed class SymbolDependencyPathsDto
{
    /// <summary>Resolved fully-qualified symbol id, or null when the symbol is unknown.</summary>
    public string? ResolvedSymbol { get; set; }

    public List<SymbolDependencyPathDto> Paths { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public sealed class ChangeModelDto
{
    /// <summary>Changed files enriched with the symbols they contain.</summary>
    public List<ChangedFileRequest> ChangedFiles { get; init; } = [];

    public List<ChangedSymbolDto> ChangedSymbols { get; init; } = [];

    public List<ChangedSymbolDto> AddedSymbols { get; init; } = [];

    public List<ChangedSymbolDto> RemovedSymbols { get; init; } = [];

    public List<ChangedSymbolDto> ModifiedSymbols { get; init; } = [];

    public List<ChangedSymbolDto> ImpactedSymbols { get; init; } = [];

    public List<DependencyEdgeDto> DependencyEdges { get; init; } = [];

    public List<ApiEndpointDto> ImpactedApis { get; init; } = [];

    public List<ExternalIntegrationImpactDto> ExternalIntegrationImpacts { get; init; } = [];

    public List<string> ImpactedServices { get; init; } = [];

    /// <summary>File paths of changed + impacted symbols — the dependency retrieval hints.</summary>
    public List<string> DependencyPaths { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}
