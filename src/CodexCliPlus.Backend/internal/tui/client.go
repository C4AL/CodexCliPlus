package tui

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// Client wraps HTTP calls to the management API.
type Client struct {
	baseURL   string
	secretKey string
	http      *http.Client
}

// NewClient creates a new management API client.
func NewClient(port int, secretKey string) *Client {
	return &Client{
		baseURL:   fmt.Sprintf("http://127.0.0.1:%d", port),
		secretKey: strings.TrimSpace(secretKey),
		http: &http.Client{
			Timeout: 10 * time.Second,
		},
	}
}

// SetSecretKey updates management API bearer token used by this client.
func (c *Client) SetSecretKey(secretKey string) {
	c.secretKey = strings.TrimSpace(secretKey)
}

func (c *Client) doRequest(method, path string, body io.Reader) ([]byte, int, error) {
	url := c.baseURL + path
	req, err := http.NewRequest(method, url, body)
	if err != nil {
		return nil, 0, err
	}
	if c.secretKey != "" {
		req.Header.Set("Authorization", "Bearer "+c.secretKey)
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := c.http.Do(req)
	if err != nil {
		return nil, 0, err
	}
	data, err := io.ReadAll(resp.Body)
	errClose := resp.Body.Close()
	if err != nil {
		return nil, resp.StatusCode, err
	}
	if errClose != nil {
		return nil, resp.StatusCode, fmt.Errorf("close response body: %w", errClose)
	}
	return data, resp.StatusCode, nil
}

func (c *Client) get(path string) ([]byte, error) {
	data, code, err := c.doRequest("GET", path, nil)
	if err != nil {
		return nil, err
	}
	if code >= 400 {
		return nil, fmt.Errorf("HTTP %d: %s", code, strings.TrimSpace(string(data)))
	}
	return data, nil
}

func (c *Client) put(path string, body io.Reader) ([]byte, error) {
	data, code, err := c.doRequest("PUT", path, body)
	if err != nil {
		return nil, err
	}
	if code >= 400 {
		return nil, fmt.Errorf("HTTP %d: %s", code, strings.TrimSpace(string(data)))
	}
	return data, nil
}

func (c *Client) patch(path string, body io.Reader) ([]byte, error) {
	data, code, err := c.doRequest("PATCH", path, body)
	if err != nil {
		return nil, err
	}
	if code >= 400 {
		return nil, fmt.Errorf("HTTP %d: %s", code, strings.TrimSpace(string(data)))
	}
	return data, nil
}

func jsonBodyReader(body any) (*bytes.Reader, error) {
	jsonBody, err := json.Marshal(body)
	if err != nil {
		return nil, err
	}
	return bytes.NewReader(jsonBody), nil
}

// getJSON fetches a path and unmarshals JSON into a generic map.
func (c *Client) getJSON(path string) (map[string]any, error) {
	data, err := c.get(path)
	if err != nil {
		return nil, err
	}
	var result map[string]any
	if err := json.Unmarshal(data, &result); err != nil {
		return nil, err
	}
	return result, nil
}

// postJSON sends a JSON body via POST and checks for errors.
func (c *Client) postJSON(path string, body any) error {
	reader, err := jsonBodyReader(body)
	if err != nil {
		return err
	}
	_, code, err := c.doRequest("POST", path, reader)
	if err != nil {
		return err
	}
	if code >= 400 {
		return fmt.Errorf("HTTP %d", code)
	}
	return nil
}

// GetConfig fetches the parsed config.
func (c *Client) GetConfig() (map[string]any, error) {
	return c.getJSON("/v0/management/config")
}

// GetConfigYAML fetches the raw config.yaml content.
func (c *Client) GetConfigYAML() (string, error) {
	data, err := c.get("/v0/management/config.yaml")
	if err != nil {
		return "", err
	}
	return string(data), nil
}

// PutConfigYAML uploads new config.yaml content.
func (c *Client) PutConfigYAML(yamlContent string) error {
	_, err := c.put("/v0/management/config.yaml", strings.NewReader(yamlContent))
	return err
}

// GetUsage fetches usage statistics.
func (c *Client) GetUsage() (map[string]any, error) {
	return c.getJSON("/v0/management/usage")
}

// GetAuthFiles lists auth credential files.
// API returns {"files": [...]}.
func (c *Client) GetAuthFiles() ([]map[string]any, error) {
	wrapper, err := c.getJSON("/v0/management/auth-files")
	if err != nil {
		return nil, err
	}
	return extractList(wrapper, "files")
}

// DeleteAuthFile deletes a single auth file by name.
func (c *Client) DeleteAuthFile(name string) error {
	query := url.Values{}
	query.Set("name", name)
	path := "/v0/management/auth-files?" + query.Encode()
	_, code, err := c.doRequest("DELETE", path, nil)
	if err != nil {
		return err
	}
	if code >= 400 {
		return fmt.Errorf("delete failed (HTTP %d)", code)
	}
	return nil
}

// ToggleAuthFile enables or disables an auth file.
func (c *Client) ToggleAuthFile(name string, disabled bool) error {
	body, err := jsonBodyReader(map[string]any{"name": name, "disabled": disabled})
	if err != nil {
		return err
	}
	_, err = c.patch("/v0/management/auth-files/status", body)
	return err
}

// PatchAuthFileFields updates editable fields on an auth file.
func (c *Client) PatchAuthFileFields(name string, fields map[string]any) error {
	bodyFields := make(map[string]any, len(fields)+1)
	for key, value := range fields {
		bodyFields[key] = value
	}
	bodyFields["name"] = name
	body, err := jsonBodyReader(bodyFields)
	if err != nil {
		return err
	}
	_, err = c.patch("/v0/management/auth-files/fields", body)
	return err
}

// GetLogs fetches log lines from the server.
func (c *Client) GetLogs(after int64, limit int) ([]string, int64, error) {
	query := url.Values{}
	if limit > 0 {
		query.Set("limit", strconv.Itoa(limit))
	}
	if after > 0 {
		query.Set("after", strconv.FormatInt(after, 10))
	}

	path := "/v0/management/logs"
	encodedQuery := query.Encode()
	if encodedQuery != "" {
		path += "?" + encodedQuery
	}

	wrapper, err := c.getJSON(path)
	if err != nil {
		return nil, after, err
	}

	lines := []string{}
	if parsedLines, errLines := extractStringList(wrapper, "lines"); errLines != nil {
		return nil, after, errLines
	} else if parsedLines != nil {
		lines = parsedLines
	}

	latest := after
	if rawLatest, ok := wrapper["latest-timestamp"]; ok {
		switch value := rawLatest.(type) {
		case float64:
			latest = int64(value)
		case json.Number:
			if parsed, errParse := value.Int64(); errParse == nil {
				latest = parsed
			}
		case int64:
			latest = value
		case int:
			latest = int64(value)
		}
	}
	if latest < after {
		latest = after
	}

	return lines, latest, nil
}

// GetAPIKeys fetches the list of API keys.
// API returns {"api-keys": [...]}.
func (c *Client) GetAPIKeys() ([]string, error) {
	wrapper, err := c.getJSON("/v0/management/api-keys")
	if err != nil {
		return nil, err
	}
	return extractStringList(wrapper, "api-keys")
}

// AddAPIKey adds a new API key by sending old=nil, new=key which appends.
func (c *Client) AddAPIKey(key string) error {
	body := map[string]any{"old": nil, "new": key}
	reader, err := jsonBodyReader(body)
	if err != nil {
		return err
	}
	_, err = c.patch("/v0/management/api-keys", reader)
	return err
}

// EditAPIKey replaces an API key at the given index.
func (c *Client) EditAPIKey(index int, newValue string) error {
	body := map[string]any{"index": index, "value": newValue}
	reader, err := jsonBodyReader(body)
	if err != nil {
		return err
	}
	_, err = c.patch("/v0/management/api-keys", reader)
	return err
}

// DeleteAPIKey deletes an API key by index.
func (c *Client) DeleteAPIKey(index int) error {
	_, code, err := c.doRequest("DELETE", fmt.Sprintf("/v0/management/api-keys?index=%d", index), nil)
	if err != nil {
		return err
	}
	if code >= 400 {
		return fmt.Errorf("delete failed (HTTP %d)", code)
	}
	return nil
}

// GetGeminiKeys fetches Gemini API keys.
// API returns {"gemini-api-key": [...]}.
func (c *Client) GetGeminiKeys() ([]map[string]any, error) {
	return c.getWrappedKeyList("/v0/management/gemini-api-key", "gemini-api-key")
}

// GetClaudeKeys fetches Claude API keys.
func (c *Client) GetClaudeKeys() ([]map[string]any, error) {
	return c.getWrappedKeyList("/v0/management/claude-api-key", "claude-api-key")
}

// GetCodexKeys fetches Codex API keys.
func (c *Client) GetCodexKeys() ([]map[string]any, error) {
	return c.getWrappedKeyList("/v0/management/codex-api-key", "codex-api-key")
}

// GetVertexKeys fetches Vertex API keys.
func (c *Client) GetVertexKeys() ([]map[string]any, error) {
	return c.getWrappedKeyList("/v0/management/vertex-api-key", "vertex-api-key")
}

// GetOpenAICompat fetches OpenAI compatibility entries.
func (c *Client) GetOpenAICompat() ([]map[string]any, error) {
	return c.getWrappedKeyList("/v0/management/openai-compatibility", "openai-compatibility")
}

// getWrappedKeyList fetches a wrapped list from the API.
func (c *Client) getWrappedKeyList(path, key string) ([]map[string]any, error) {
	wrapper, err := c.getJSON(path)
	if err != nil {
		return nil, err
	}
	return extractList(wrapper, key)
}

// extractList pulls an array of maps from a wrapper object by key.
func extractList(wrapper map[string]any, key string) ([]map[string]any, error) {
	arr, ok := wrapper[key]
	if !ok || arr == nil {
		return nil, nil
	}
	switch values := arr.(type) {
	case []map[string]any:
		return cloneMapList(values), nil
	case []any:
		result := make([]map[string]any, 0, len(values))
		for i, item := range values {
			if item == nil {
				result = append(result, nil)
				continue
			}
			entry, ok := item.(map[string]any)
			if !ok {
				return nil, fmt.Errorf("%s[%d] must be object", key, i)
			}
			result = append(result, cloneMap(entry))
		}
		return result, nil
	default:
		return nil, fmt.Errorf("%s must be array", key)
	}
}

func cloneMapList(values []map[string]any) []map[string]any {
	result := make([]map[string]any, 0, len(values))
	for _, value := range values {
		result = append(result, cloneMap(value))
	}
	return result
}

func cloneMap(value map[string]any) map[string]any {
	if value == nil {
		return nil
	}
	out := make(map[string]any, len(value))
	for key, item := range value {
		out[key] = item
	}
	return out
}

func extractStringList(wrapper map[string]any, key string) ([]string, error) {
	arr, ok := wrapper[key]
	if !ok || arr == nil {
		return nil, nil
	}
	switch values := arr.(type) {
	case []string:
		return append([]string(nil), values...), nil
	case []any:
		result := make([]string, 0, len(values))
		for i, item := range values {
			if item == nil {
				result = append(result, "")
				continue
			}
			value, ok := item.(string)
			if !ok {
				return nil, fmt.Errorf("%s[%d] must be string", key, i)
			}
			result = append(result, value)
		}
		return result, nil
	default:
		return nil, fmt.Errorf("%s must be array", key)
	}
}

// GetDebug fetches the current debug setting.
func (c *Client) GetDebug() (bool, error) {
	wrapper, err := c.getJSON("/v0/management/debug")
	if err != nil {
		return false, err
	}
	if v, ok := wrapper["debug"]; ok {
		if b, ok := v.(bool); ok {
			return b, nil
		}
	}
	return false, nil
}

// GetAuthStatus polls the OAuth session status.
// Returns status ("wait", "ok", "error") and optional error message.
func (c *Client) GetAuthStatus(state string) (string, string, error) {
	query := url.Values{}
	query.Set("state", state)
	path := "/v0/management/get-auth-status?" + query.Encode()
	wrapper, err := c.getJSON(path)
	if err != nil {
		return "", "", err
	}
	status := getString(wrapper, "status")
	errMsg := getString(wrapper, "error")
	return status, errMsg, nil
}

// ----- Config field update methods -----

// PutBoolField updates a boolean config field.
func (c *Client) PutBoolField(path string, value bool) error {
	body, err := jsonBodyReader(map[string]any{"value": value})
	if err != nil {
		return err
	}
	_, err = c.put("/v0/management/"+path, body)
	return err
}

// PutIntField updates an integer config field.
func (c *Client) PutIntField(path string, value int) error {
	body, err := jsonBodyReader(map[string]any{"value": value})
	if err != nil {
		return err
	}
	_, err = c.put("/v0/management/"+path, body)
	return err
}

// PutStringField updates a string config field.
func (c *Client) PutStringField(path string, value string) error {
	body, err := jsonBodyReader(map[string]any{"value": value})
	if err != nil {
		return err
	}
	_, err = c.put("/v0/management/"+path, body)
	return err
}
