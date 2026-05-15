package config

import (
	"strings"

	"gopkg.in/yaml.v3"
)

func (cfg *Config) ApplyCodexCliPlusGPTOnlyConfig() {
	if cfg == nil {
		return
	}

	cfg.GeminiKey = nil
	cfg.ClaudeKey = nil
	cfg.VertexCompatAPIKey = nil
	cfg.AmpCode = AmpCode{}
	cfg.ClaudeHeaderDefaults = ClaudeHeaderDefaults{}
	cfg.AntigravitySignatureCacheEnabled = nil
	cfg.AntigravitySignatureBypassStrict = nil
	cfg.QuotaExceeded.AntigravityCredits = false
	cfg.OAuthExcludedModels = codexCliPlusKeepGPTOnlyStringSlices(cfg.OAuthExcludedModels)
	cfg.OAuthModelAlias = codexCliPlusKeepGPTOnlyModelAliases(cfg.OAuthModelAlias)
}

func CodexCliPlusGPTOnlyConfigForWrite(cfg *Config) (*Config, error) {
	if cfg == nil {
		return cfg, nil
	}
	data, err := yaml.Marshal(cfg)
	if err != nil {
		return nil, err
	}
	var out Config
	if err := yaml.Unmarshal(data, &out); err != nil {
		return nil, err
	}
	out.ApplyCodexCliPlusGPTOnlyConfig()
	return &out, nil
}

func PruneCodexCliPlusGPTOnlyConfigYAML(root *yaml.Node) {
	if root == nil || root.Kind != yaml.MappingNode {
		return
	}

	for _, key := range []string{
		"gemini-api-key",
		"claude-api-key",
		"vertex-api-key",
		"ampcode",
		"claude-header-defaults",
		"antigravity-signature-cache-enabled",
		"antigravity-signature-bypass-strict",
		"generative-language-api-key",
		"amp-upstream-url",
		"amp-upstream-api-key",
		"amp-restrict-management-to-localhost",
		"amp-model-mappings",
	} {
		removeMapKey(root, key)
	}

	if idx := findMapKeyIndex(root, "quota-exceeded"); idx >= 0 && idx+1 < len(root.Content) {
		quota := root.Content[idx+1]
		removeMapKey(quota, "antigravity-credits")
		if quota == nil || quota.Kind != yaml.MappingNode || len(quota.Content) == 0 {
			removeMapKey(root, "quota-exceeded")
		}
	}

	codexCliPlusPruneGPTOnlyProviderMapping(root, "oauth-excluded-models")
	codexCliPlusPruneGPTOnlyProviderMapping(root, "oauth-model-alias")
}

func codexCliPlusKeepGPTOnlyStringSlices(in map[string][]string) map[string][]string {
	if len(in) == 0 {
		return nil
	}
	out := make(map[string][]string, len(in))
	for provider, models := range in {
		key := strings.ToLower(strings.TrimSpace(provider))
		if !codexCliPlusGPTOnlyProviderAllowed(key) || len(models) == 0 {
			continue
		}
		out[key] = models
	}
	if len(out) == 0 {
		return nil
	}
	return out
}

func codexCliPlusKeepGPTOnlyModelAliases(in map[string][]OAuthModelAlias) map[string][]OAuthModelAlias {
	if len(in) == 0 {
		return nil
	}
	out := make(map[string][]OAuthModelAlias, len(in))
	for provider, aliases := range in {
		key := strings.ToLower(strings.TrimSpace(provider))
		if !codexCliPlusGPTOnlyProviderAllowed(key) || len(aliases) == 0 {
			continue
		}
		out[key] = aliases
	}
	if len(out) == 0 {
		return nil
	}
	return out
}

func codexCliPlusPruneGPTOnlyProviderMapping(root *yaml.Node, key string) {
	idx := findMapKeyIndex(root, key)
	if idx < 0 || idx+1 >= len(root.Content) {
		return
	}
	value := root.Content[idx+1]
	if value == nil || value.Kind != yaml.MappingNode {
		removeMapKey(root, key)
		return
	}
	for i := 0; i+1 < len(value.Content); {
		keyNode := value.Content[i]
		if keyNode == nil || !codexCliPlusGPTOnlyProviderAllowed(keyNode.Value) {
			value.Content = append(value.Content[:i], value.Content[i+2:]...)
			continue
		}
		i += 2
	}
	if len(value.Content) == 0 {
		removeMapKey(root, key)
	}
}

func codexCliPlusGPTOnlyProviderAllowed(provider string) bool {
	switch strings.ToLower(strings.TrimSpace(provider)) {
	case "codex", "openai", "openai-compatibility":
		return true
	default:
		return false
	}
}
