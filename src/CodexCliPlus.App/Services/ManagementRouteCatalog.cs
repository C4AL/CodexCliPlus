using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.Services;

public static class ManagementRouteCatalog
{
    public static IReadOnlyList<ManagementRouteDefinition> All { get; } =
    [
        new("dashboard", "/", "仪表盘", true),
        new("config", "/config", "配置", true),
        new("ai-providers", "/ai-providers", "账号配置", true),
        new("auth-files", "/auth-files", "认证文件", true),
        new("quota", "/quota", "配额", true),
        new("usage", "/usage", "用量", true),
        new("logs", "/logs", "日志", true),
        new("system", "/system", "系统", true),

        new("ai-providers-gemini-new", "/ai-providers/gemini/new", "新增 Gemini", false, "ai-providers"),
        new("ai-providers-gemini-edit", "/ai-providers/gemini/:index", "编辑 Gemini", false, "ai-providers"),
        new("ai-providers-codex-new", "/ai-providers/codex/new", "新增 Codex", false, "ai-providers"),
        new("ai-providers-codex-edit", "/ai-providers/codex/:index", "编辑 Codex", false, "ai-providers"),
        new("ai-providers-claude-new", "/ai-providers/claude/new", "新增 Claude", false, "ai-providers"),
        new("ai-providers-claude-edit", "/ai-providers/claude/:index", "编辑 Claude", false, "ai-providers"),
        new("ai-providers-claude-models", "/ai-providers/claude/models", "Claude 模型发现", false, "ai-providers"),
        new("ai-providers-vertex-new", "/ai-providers/vertex/new", "新增 Vertex", false, "ai-providers"),
        new("ai-providers-vertex-edit", "/ai-providers/vertex/:index", "编辑 Vertex", false, "ai-providers"),
        new("ai-providers-openai-new", "/ai-providers/openai/new", "新增 OpenAI 兼容", false, "ai-providers"),
        new("ai-providers-openai-edit", "/ai-providers/openai/:index", "编辑 OpenAI 兼容", false, "ai-providers"),
        new("ai-providers-openai-models", "/ai-providers/openai/models", "OpenAI 模型发现", false, "ai-providers"),
        new("ai-providers-ampcode", "/ai-providers/ampcode", "Ampcode", false, "ai-providers"),

        new("auth-files-oauth-excluded", "/auth-files/oauth-excluded", "OAuth 排除模型", false, "auth-files"),
        new("auth-files-oauth-model-alias", "/auth-files/oauth-model-alias", "OAuth 模型别名", false, "auth-files")
    ];

    public static IReadOnlyList<ManagementRouteDefinition> Primary { get; } =
        All.Where(route => route.IsPrimary).ToArray();
}
