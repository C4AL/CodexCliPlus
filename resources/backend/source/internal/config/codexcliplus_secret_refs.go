package config

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"strings"
	"time"

	"gopkg.in/yaml.v3"
)

const (
	codexCliPlusSecretBrokerURLEnv   = "CCP_SECRET_BROKER_URL"
	codexCliPlusSecretBrokerTokenEnv = "CCP_SECRET_BROKER_TOKEN"
)

type codexCliPlusSecretResolver struct {
	baseURL string
	token   string
	client  *http.Client
}

type codexCliPlusSecretResponse struct {
	Value string `json:"value"`
}

type codexCliPlusSecretSaveResponse struct {
	URI      string `json:"uri"`
	SecretID string `json:"secretId"`
}

func newCodexCliPlusSecretResolver() *codexCliPlusSecretResolver {
	baseURL := strings.TrimRight(strings.TrimSpace(os.Getenv(codexCliPlusSecretBrokerURLEnv)), "/")
	token := strings.TrimSpace(os.Getenv(codexCliPlusSecretBrokerTokenEnv))
	if baseURL == "" || token == "" {
		return nil
	}
	return &codexCliPlusSecretResolver{
		baseURL: baseURL,
		token:   token,
		client:  &http.Client{Timeout: 5 * time.Second},
	}
}

func ResolveCodexCliPlusSecretRefs(cfg *Config) error {
	resolver := newCodexCliPlusSecretResolver()
	if cfg == nil || resolver == nil {
		return nil
	}

	var err error
	if cfg.RemoteManagement.SecretKey, err = resolver.resolve("remote-management.secret-key", cfg.RemoteManagement.SecretKey); err != nil {
		return err
	}
	for i := range cfg.APIKeys {
		if cfg.APIKeys[i], err = resolver.resolve(fmt.Sprintf("api-keys[%d]", i), cfg.APIKeys[i]); err != nil {
			return err
		}
	}
	for i := range cfg.GeminiKey {
		if cfg.GeminiKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("gemini-api-key[%d].api-key", i), cfg.GeminiKey[i].APIKey); err != nil {
			return err
		}
		if err = resolver.resolveMap(fmt.Sprintf("gemini-api-key[%d].headers", i), cfg.GeminiKey[i].Headers); err != nil {
			return err
		}
	}
	for i := range cfg.CodexKey {
		if cfg.CodexKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("codex-api-key[%d].api-key", i), cfg.CodexKey[i].APIKey); err != nil {
			return err
		}
		if err = resolver.resolveMap(fmt.Sprintf("codex-api-key[%d].headers", i), cfg.CodexKey[i].Headers); err != nil {
			return err
		}
	}
	for i := range cfg.ClaudeKey {
		if cfg.ClaudeKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("claude-api-key[%d].api-key", i), cfg.ClaudeKey[i].APIKey); err != nil {
			return err
		}
		if err = resolver.resolveMap(fmt.Sprintf("claude-api-key[%d].headers", i), cfg.ClaudeKey[i].Headers); err != nil {
			return err
		}
	}
	for i := range cfg.VertexCompatAPIKey {
		if cfg.VertexCompatAPIKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("vertex-api-key[%d].api-key", i), cfg.VertexCompatAPIKey[i].APIKey); err != nil {
			return err
		}
		if err = resolver.resolveMap(fmt.Sprintf("vertex-api-key[%d].headers", i), cfg.VertexCompatAPIKey[i].Headers); err != nil {
			return err
		}
	}
	for i := range cfg.OpenAICompatibility {
		if err = resolver.resolveMap(fmt.Sprintf("openai-compatibility[%d].headers", i), cfg.OpenAICompatibility[i].Headers); err != nil {
			return err
		}
		for j := range cfg.OpenAICompatibility[i].APIKeyEntries {
			if cfg.OpenAICompatibility[i].APIKeyEntries[j].APIKey, err = resolver.resolve(fmt.Sprintf("openai-compatibility[%d].api-key-entries[%d].api-key", i, j), cfg.OpenAICompatibility[i].APIKeyEntries[j].APIKey); err != nil {
				return err
			}
		}
	}
	if cfg.AmpCode.UpstreamAPIKey, err = resolver.resolve("ampcode.upstream-api-key", cfg.AmpCode.UpstreamAPIKey); err != nil {
		return err
	}
	for i := range cfg.AmpCode.UpstreamAPIKeys {
		if cfg.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, err = resolver.resolve(fmt.Sprintf("ampcode.upstream-api-keys[%d].upstream-api-key", i), cfg.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey); err != nil {
			return err
		}
		for j := range cfg.AmpCode.UpstreamAPIKeys[i].APIKeys {
			if cfg.AmpCode.UpstreamAPIKeys[i].APIKeys[j], err = resolver.resolve(fmt.Sprintf("ampcode.upstream-api-keys[%d].api-keys[%d]", i, j), cfg.AmpCode.UpstreamAPIKeys[i].APIKeys[j]); err != nil {
				return err
			}
		}
	}
	return nil
}

func ResolveCodexCliPlusSecretRefForRead(path, value string) (string, error) {
	resolver := newCodexCliPlusSecretResolver()
	if resolver == nil {
		return value, nil
	}
	return resolver.resolve(path, value)
}

func ProtectCodexCliPlusConfigYAMLForWrite(data []byte) ([]byte, error) {
	resolver := newCodexCliPlusSecretResolver()
	if resolver == nil || len(bytes.TrimSpace(data)) == 0 {
		return data, nil
	}
	var cfg Config
	if err := yaml.Unmarshal(data, &cfg); err != nil {
		return nil, err
	}
	gptOnlyCfg, err := CodexCliPlusGPTOnlyConfigForWrite(&cfg)
	if err != nil {
		return nil, err
	}
	protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(gptOnlyCfg)
	if err != nil {
		return nil, err
	}
	if protectedCfg == nil {
		return data, nil
	}
	protectedData, err := yaml.Marshal(protectedCfg)
	if err != nil {
		return nil, err
	}
	return protectedData, nil
}

func ProtectCodexCliPlusAuthJSONForWrite(data []byte) ([]byte, error) {
	resolver := newCodexCliPlusSecretResolver()
	if resolver == nil || len(bytes.TrimSpace(data)) == 0 {
		return data, nil
	}

	var payload any
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	if err := decoder.Decode(&payload); err != nil {
		return nil, err
	}

	changed, err := resolver.protectJSONNode("$", payload, false, "")
	if err != nil {
		return nil, err
	}
	if !changed {
		return data, nil
	}

	protectedData, err := json.MarshalIndent(payload, "", "  ")
	if err != nil {
		return nil, err
	}
	return append(protectedData, '\n'), nil
}

func ProtectCodexCliPlusSecretRefsForWrite(cfg *Config) (*Config, error) {
	resolver := newCodexCliPlusSecretResolver()
	if cfg == nil || resolver == nil {
		return cfg, nil
	}

	protected, err := cloneCodexCliPlusConfig(cfg)
	if err != nil {
		return nil, err
	}

	if protected.RemoteManagement.SecretKey, err = resolver.protect("remote-management.secret-key", protected.RemoteManagement.SecretKey, "ManagementKey"); err != nil {
		return nil, err
	}
	for i := range protected.APIKeys {
		if protected.APIKeys[i], err = resolver.protect("api-keys", protected.APIKeys[i], "ApiKey"); err != nil {
			return nil, err
		}
	}
	for i := range protected.GeminiKey {
		if protected.GeminiKey[i].APIKey, err = resolver.protect(fmt.Sprintf("gemini-api-key[%d].api-key", i), protected.GeminiKey[i].APIKey, "ApiKey"); err != nil {
			return nil, err
		}
		if err = resolver.protectMap(fmt.Sprintf("gemini-api-key[%d].headers", i), protected.GeminiKey[i].Headers, "Header"); err != nil {
			return nil, err
		}
	}
	for i := range protected.CodexKey {
		if protected.CodexKey[i].APIKey, err = resolver.protect(fmt.Sprintf("codex-api-key[%d].api-key", i), protected.CodexKey[i].APIKey, "ApiKey"); err != nil {
			return nil, err
		}
		if err = resolver.protectMap(fmt.Sprintf("codex-api-key[%d].headers", i), protected.CodexKey[i].Headers, "Header"); err != nil {
			return nil, err
		}
	}
	for i := range protected.ClaudeKey {
		if protected.ClaudeKey[i].APIKey, err = resolver.protect(fmt.Sprintf("claude-api-key[%d].api-key", i), protected.ClaudeKey[i].APIKey, "ApiKey"); err != nil {
			return nil, err
		}
		if err = resolver.protectMap(fmt.Sprintf("claude-api-key[%d].headers", i), protected.ClaudeKey[i].Headers, "Header"); err != nil {
			return nil, err
		}
	}
	for i := range protected.VertexCompatAPIKey {
		if protected.VertexCompatAPIKey[i].APIKey, err = resolver.protect(fmt.Sprintf("vertex-api-key[%d].api-key", i), protected.VertexCompatAPIKey[i].APIKey, "ApiKey"); err != nil {
			return nil, err
		}
		if err = resolver.protectMap(fmt.Sprintf("vertex-api-key[%d].headers", i), protected.VertexCompatAPIKey[i].Headers, "Header"); err != nil {
			return nil, err
		}
	}
	for i := range protected.OpenAICompatibility {
		if err = resolver.protectMap(fmt.Sprintf("openai-compatibility[%d].headers", i), protected.OpenAICompatibility[i].Headers, "Header"); err != nil {
			return nil, err
		}
		for j := range protected.OpenAICompatibility[i].APIKeyEntries {
			if protected.OpenAICompatibility[i].APIKeyEntries[j].APIKey, err = resolver.protect(fmt.Sprintf("openai-compatibility[%d].api-key-entries[%d].api-key", i, j), protected.OpenAICompatibility[i].APIKeyEntries[j].APIKey, "ApiKey"); err != nil {
				return nil, err
			}
		}
	}
	if protected.AmpCode.UpstreamAPIKey, err = resolver.protect("ampcode.upstream-api-key", protected.AmpCode.UpstreamAPIKey, "ApiKey"); err != nil {
		return nil, err
	}
	for i := range protected.AmpCode.UpstreamAPIKeys {
		if protected.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, err = resolver.protect(fmt.Sprintf("ampcode.upstream-api-keys[%d].upstream-api-key", i), protected.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, "ApiKey"); err != nil {
			return nil, err
		}
		for j := range protected.AmpCode.UpstreamAPIKeys[i].APIKeys {
			if protected.AmpCode.UpstreamAPIKeys[i].APIKeys[j], err = resolver.protect(fmt.Sprintf("ampcode.upstream-api-keys[%d].api-keys[%d]", i, j), protected.AmpCode.UpstreamAPIKeys[i].APIKeys[j], "ApiKey"); err != nil {
				return nil, err
			}
		}
	}
	return protected, nil
}

func cloneCodexCliPlusConfig(cfg *Config) (*Config, error) {
	data, err := yaml.Marshal(cfg)
	if err != nil {
		return nil, err
	}
	var protected Config
	if err := yaml.Unmarshal(data, &protected); err != nil {
		return nil, err
	}
	return &protected, nil
}

func (r *codexCliPlusSecretResolver) protectMap(path string, values map[string]string, kind string) error {
	for key, value := range values {
		protected, err := r.protect(path+"."+key, value, kind)
		if err != nil {
			return err
		}
		values[key] = protected
	}
	return nil
}

func (r *codexCliPlusSecretResolver) protect(path, value, kind string) (string, error) {
	return r.protectWithSource(path, value, kind, "backend-config-write")
}

func (r *codexCliPlusSecretResolver) protectWithSource(path, value, kind, source string) (string, error) {
	trimmed := strings.TrimSpace(value)
	if trimmed == "" {
		return value, nil
	}
	if _, ok := codexCliPlusSecretID(trimmed); ok {
		return value, nil
	}

	requestURL := r.baseURL + "/v1/secrets"
	payload := map[string]any{
		"value":  value,
		"kind":   kind,
		"source": source,
		"metadata": map[string]string{
			"path": path,
		},
	}
	body, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	req, err := http.NewRequest(http.MethodPost, requestURL, bytes.NewReader(body))
	if err != nil {
		return "", err
	}
	req.Header.Set("Authorization", "Bearer "+r.token)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Accept", "application/json")

	resp, err := r.client.Do(req)
	if err != nil {
		return "", fmt.Errorf("secret save unavailable for %s: %w", path, err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusCreated && resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("secret save unavailable for %s: %s", path, resp.Status)
	}
	var saved codexCliPlusSecretSaveResponse
	if err := json.NewDecoder(resp.Body).Decode(&saved); err != nil {
		return "", err
	}
	if strings.TrimSpace(saved.URI) != "" {
		return saved.URI, nil
	}
	if strings.TrimSpace(saved.SecretID) != "" {
		return "ccp-secret://" + strings.TrimSpace(saved.SecretID), nil
	}
	return "", fmt.Errorf("secret save for %s returned empty reference", path)
}

func (r *codexCliPlusSecretResolver) protectJSONNode(path string, value any, parentSensitive bool, parentKey string) (bool, error) {
	switch node := value.(type) {
	case map[string]any:
		changed := false
		for key, child := range node {
			sensitive := parentSensitive || codexCliPlusSensitiveKey(key)
			if strings.EqualFold(parentKey, "headers") && codexCliPlusSensitiveHeaderKey(key) {
				sensitive = true
			}
			childPath := path + "." + key
			if text, ok := child.(string); ok && sensitive {
				protected, err := r.protectWithSource(childPath, text, codexCliPlusSecretKind(key, parentKey), "backend-auth-write")
				if err != nil {
					return false, err
				}
				if protected != text {
					node[key] = protected
					changed = true
				}
				continue
			}
			childChanged, err := r.protectJSONNode(childPath, child, sensitive, key)
			if err != nil {
				return false, err
			}
			changed = changed || childChanged
		}
		return changed, nil
	case []any:
		changed := false
		for i, child := range node {
			childPath := fmt.Sprintf("%s[%d]", path, i)
			if text, ok := child.(string); ok && parentSensitive {
				protected, err := r.protectWithSource(childPath, text, codexCliPlusSecretKind(parentKey, ""), "backend-auth-write")
				if err != nil {
					return false, err
				}
				if protected != text {
					node[i] = protected
					changed = true
				}
				continue
			}
			childChanged, err := r.protectJSONNode(childPath, child, parentSensitive, parentKey)
			if err != nil {
				return false, err
			}
			changed = changed || childChanged
		}
		return changed, nil
	default:
		return false, nil
	}
}

func codexCliPlusSensitiveKey(key string) bool {
	normalized := codexCliPlusNormalizeKey(key)
	if normalized == "" {
		return false
	}
	switch normalized {
	case "api-key", "api-keys", "apikey", "access-token", "refresh-token", "id-token",
		"token", "tokens", "authorization", "cookie", "secret", "secret-key",
		"client-secret", "private-key", "upstream-api-key", "upstream-api-keys",
		"x-api-key", "proxy-authorization":
		return true
	}
	return strings.HasSuffix(normalized, "-token") ||
		strings.HasSuffix(normalized, "-secret") ||
		strings.HasSuffix(normalized, "-api-key")
}

func codexCliPlusSensitiveHeaderKey(key string) bool {
	normalized := strings.ToLower(strings.TrimSpace(key))
	switch normalized {
	case "authorization", "cookie", "x-api-key", "x-goog-api-key", "proxy-authorization":
		return true
	}
	return codexCliPlusSensitiveKey(key) ||
		strings.Contains(normalized, "token") ||
		strings.Contains(normalized, "secret")
}

func codexCliPlusNormalizeKey(key string) string {
	normalized := strings.ToLower(strings.TrimSpace(key))
	normalized = strings.ReplaceAll(normalized, "_", "-")
	normalized = strings.ReplaceAll(normalized, " ", "-")
	for strings.Contains(normalized, "--") {
		normalized = strings.ReplaceAll(normalized, "--", "-")
	}
	return normalized
}

func codexCliPlusSecretKind(key, parentKey string) string {
	normalizedKey := codexCliPlusNormalizeKey(key)
	normalizedParent := codexCliPlusNormalizeKey(parentKey)
	switch {
	case strings.Contains(normalizedKey, "refresh-token"):
		return "OAuthRefreshToken"
	case strings.Contains(normalizedKey, "access-token") || strings.Contains(normalizedKey, "id-token"):
		return "OAuthAccessToken"
	case strings.Contains(normalizedKey, "authorization") || strings.Contains(normalizedParent, "authorization"):
		return "AuthorizationHeader"
	case strings.Contains(normalizedKey, "cookie"):
		return "Cookie"
	case strings.Contains(normalizedKey, "api-key"):
		return "ApiKey"
	case normalizedParent == "headers":
		return "Header"
	case strings.Contains(normalizedKey, "private-key") || strings.Contains(normalizedKey, "secret"):
		return "ProviderCredential"
	case strings.Contains(normalizedKey, "token"):
		return "OAuthToken"
	default:
		return "Unknown"
	}
}

func (r *codexCliPlusSecretResolver) resolveMap(path string, values map[string]string) error {
	for key, value := range values {
		resolved, err := r.resolve(path+"."+key, value)
		if err != nil {
			return err
		}
		values[key] = resolved
	}
	return nil
}

func (r *codexCliPlusSecretResolver) resolve(path, value string) (string, error) {
	secretID, ok := codexCliPlusSecretID(value)
	if !ok {
		return value, nil
	}
	requestURL := r.baseURL + "/v1/secrets/" + url.PathEscape(secretID)
	req, err := http.NewRequest(http.MethodGet, requestURL, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("Authorization", "Bearer "+r.token)

	resp, err := r.client.Do(req)
	if err != nil {
		return "", fmt.Errorf("secret_ref %s unavailable for %s: %w", secretID, path, err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("secret_ref %s unavailable for %s: %s", secretID, path, resp.Status)
	}
	var payload codexCliPlusSecretResponse
	if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
		return "", err
	}
	if strings.TrimSpace(payload.Value) == "" {
		return "", fmt.Errorf("secret_ref %s for %s resolved to empty value", secretID, path)
	}
	return payload.Value, nil
}

func codexCliPlusSecretID(value string) (string, bool) {
	trimmed := strings.TrimSpace(value)
	for _, prefix := range []string{"ccp-secret://", "vault://"} {
		if strings.HasPrefix(strings.ToLower(trimmed), prefix) {
			id := strings.TrimSpace(trimmed[len(prefix):])
			return id, id != ""
		}
	}
	return "", false
}
