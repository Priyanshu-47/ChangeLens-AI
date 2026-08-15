namespace ChangeLens.Infrastructure.Analysis;

/// <summary>Normalized symbol kinds produced by the Roslyn analyzer (ADR-0011).</summary>
public enum SymbolKind
{
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Method,
    Constructor,
    Property,
    Parameter,
    Field
}

/// <summary>Normalized representation of one analyzed symbol (docs/rag-architecture.md §8).</summary>
public sealed record SymbolInfo(
    string SymbolId,
    SymbolKind Kind,
    string Name,
    string FullyQualifiedName,
    string? FilePath,
    string? Namespace,
    string? Project,
    string? Signature,
    string? ReturnType,
    IReadOnlyList<string> Parameters,
    string? DeclarationHash = null)
{
    /// <summary>Human-readable id for the evidence index, e.g. <c>symbol:AcmePay.Application.Payments.ProcessPaymentHandler.HandleAsync(...)</c>.</summary>
    public string EvidenceId => $"symbol:{FullyQualifiedName}";
}

/// <summary>Dependency edge types — only edges Roslyn can actually prove are created.</summary>
public enum EdgeType
{
    /// <summary>Method/constructor invocation (CALLS).</summary>
    Calls,

    /// <summary>Direct reference to a repo type (object creation, field/parameter/return type).</summary>
    ReferencesType,

    /// <summary>Class implements an interface.</summary>
    Implements,

    /// <summary>Class inherits from a base class.</summary>
    Inherits
}

/// <summary>One directed dependency edge between two repo symbols.</summary>
public sealed record DependencyEdge(
    string FromSymbolId,
    string ToSymbolId,
    EdgeType Type,
    string? FilePath)
{
    /// <summary>Stable evidence id, e.g. <c>dependency:AcmePay...PaymentService -> AcmePay...PaymentGatewayClient</c>.</summary>
    public string EvidenceId => $"dependency:{FromSymbolId} -> {ToSymbolId}";
}
