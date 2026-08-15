using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Tools;

/// <summary>
/// The allowlisted tool registry (docs/agent-tools.md §4). Only tools registered here
/// can be proposed by the AI; unknown names become TOOL_NOT_ALLOWED. Registration is
/// explicit DI wiring — never dynamic discovery of arbitrary code.
/// </summary>
public sealed class ToolRegistry(IEnumerable<ITool> tools)
{
    private readonly IReadOnlyDictionary<string, ITool> _byName = tools
        .GroupBy(t => t.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public IReadOnlyList<ITool> Tools => _byName.Values.ToList();

    public ITool? TryGet(string name)
        => name is not null && _byName.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>The model-facing catalog (name/description/input schema).</summary>
    public List<ToolDefinitionDto> Describe() => _byName.Values
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .Select(t => new ToolDefinitionDto
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        })
        .ToList();
}
