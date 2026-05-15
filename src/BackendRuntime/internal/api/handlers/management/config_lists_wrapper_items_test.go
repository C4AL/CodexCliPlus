package management

import (
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
)

func TestPutListWrappersAllowEmptyItemsToClear(t *testing.T) {
	gin.SetMode(gin.TestMode)

	tests := []struct {
		name      string
		path      string
		cfg       *config.Config
		call      func(*Handler, *gin.Context)
		remaining func(*config.Config) int
	}{
		{
			name: "api keys",
			path: "/v0/management/api-keys",
			cfg: &config.Config{
				SDKConfig: config.SDKConfig{APIKeys: []string{"key-a"}},
			},
			call:      (*Handler).PutAPIKeys,
			remaining: func(cfg *config.Config) int { return len(cfg.APIKeys) },
		},
		{
			name: "gemini keys",
			path: "/v0/management/gemini-api-key",
			cfg: &config.Config{
				GeminiKey: []config.GeminiKey{{APIKey: "key-a"}},
			},
			call:      (*Handler).PutGeminiKeys,
			remaining: func(cfg *config.Config) int { return len(cfg.GeminiKey) },
		},
		{
			name: "claude keys",
			path: "/v0/management/claude-api-key",
			cfg: &config.Config{
				ClaudeKey: []config.ClaudeKey{{APIKey: "key-a"}},
			},
			call:      (*Handler).PutClaudeKeys,
			remaining: func(cfg *config.Config) int { return len(cfg.ClaudeKey) },
		},
		{
			name: "openai compatibility",
			path: "/v0/management/openai-compatibility",
			cfg: &config.Config{
				OpenAICompatibility: []config.OpenAICompatibility{{Name: "provider-a", BaseURL: "https://api.example.com"}},
			},
			call:      (*Handler).PutOpenAICompat,
			remaining: func(cfg *config.Config) int { return len(cfg.OpenAICompatibility) },
		},
		{
			name: "vertex compat keys",
			path: "/v0/management/vertex-api-key",
			cfg: &config.Config{
				VertexCompatAPIKey: []config.VertexCompatKey{{APIKey: "key-a"}},
			},
			call:      (*Handler).PutVertexCompatKeys,
			remaining: func(cfg *config.Config) int { return len(cfg.VertexCompatAPIKey) },
		},
		{
			name: "codex keys",
			path: "/v0/management/codex-api-key",
			cfg: &config.Config{
				CodexKey: []config.CodexKey{{APIKey: "key-a", BaseURL: "https://api.example.com"}},
			},
			call:      (*Handler).PutCodexKeys,
			remaining: func(cfg *config.Config) int { return len(cfg.CodexKey) },
		},
	}

	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			h := &Handler{
				cfg:            tc.cfg,
				configFilePath: writeTestConfigFile(t),
			}

			rec := httptest.NewRecorder()
			c, _ := gin.CreateTestContext(rec)
			c.Request = httptest.NewRequest(http.MethodPut, tc.path, strings.NewReader(`{"items":[]}`))

			tc.call(h, c)

			if rec.Code != http.StatusOK {
				t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
			}
			if got := tc.remaining(h.cfg); got != 0 {
				t.Fatalf("remaining items = %d, want 0", got)
			}
		})
	}
}

func TestPutListWrapperRejectsMissingItemsWithoutMutating(t *testing.T) {
	gin.SetMode(gin.TestMode)

	existing := []string{"key-a"}
	h := &Handler{
		cfg: &config.Config{
			SDKConfig: config.SDKConfig{APIKeys: append([]string(nil), existing...)},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPut, "/v0/management/api-keys", strings.NewReader(`{"other":[]}`))

	h.PutAPIKeys(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.APIKeys, existing) {
		t.Fatalf("api keys = %#v, want %#v", h.cfg.APIKeys, existing)
	}
}
