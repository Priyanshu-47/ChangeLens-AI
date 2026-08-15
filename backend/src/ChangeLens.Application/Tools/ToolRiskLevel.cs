namespace ChangeLens.Application.Tools;

/// <summary>
/// Tool risk classification (docs/agent-tools.md §6). Phase 8 implements only
/// LOW-risk read-only tools; the policy layer supports future higher-risk tools
/// (which would require explicit approval) without redesign.
/// </summary>
public enum ToolRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}
