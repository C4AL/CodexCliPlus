package management

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

func TestListAuthFiles_CodexSecretIDTokenReturnsAccountFields(t *testing.T) {
	t.Setenv("MANAGEMENT_PASSWORD", "")
	gin.SetMode(gin.TestMode)

	idToken := makeCodexIDToken(t, "chatgpt-account-secret", "plus")
	secretServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if got := r.Header.Get("Authorization"); got != "Bearer test-secret-token" {
			t.Fatalf("Authorization = %q, want bearer token", got)
		}
		if r.URL.Path != "/v1/secrets/codex-id-token" {
			t.Fatalf("secret path = %q", r.URL.Path)
		}
		_ = json.NewEncoder(w).Encode(map[string]string{"value": idToken})
	}))
	t.Cleanup(secretServer.Close)
	t.Setenv("CCP_SECRET_BROKER_URL", secretServer.URL)
	t.Setenv("CCP_SECRET_BROKER_TOKEN", "test-secret-token")

	manager := coreauth.NewManager(nil, nil, nil)
	if _, errRegister := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "codex-secret",
		FileName: "codex-secret.json",
		Provider: "codex",
		Attributes: map[string]string{
			"runtime_only": "true",
		},
		Metadata: map[string]any{
			"id_token": "ccp-secret://codex-id-token",
		},
	}); errRegister != nil {
		t.Fatalf("register auth: %v", errRegister)
	}

	entry := listSingleAuthFile(t, manager)

	if got := entry["chatgpt_account_id"]; got != "chatgpt-account-secret" {
		t.Fatalf("chatgpt_account_id = %#v", got)
	}
	if got := entry["plan_type"]; got != "plus" {
		t.Fatalf("plan_type = %#v", got)
	}

	idTokenClaims, ok := entry["id_token"].(map[string]any)
	if !ok {
		t.Fatalf("id_token claims missing: %#v", entry["id_token"])
	}
	if got := idTokenClaims["chatgpt_account_id"]; got != "chatgpt-account-secret" {
		t.Fatalf("id_token.chatgpt_account_id = %#v", got)
	}
	if text := strings.TrimSpace(toJSON(t, entry)); strings.Contains(text, idToken) || strings.Contains(text, "ccp-secret://") {
		t.Fatalf("auth file response exposed token material: %s", text)
	}
}

func TestListAuthFiles_CodexVaultIDTokenReturnsAccountFields(t *testing.T) {
	t.Setenv("MANAGEMENT_PASSWORD", "")
	gin.SetMode(gin.TestMode)

	idToken := makeCodexIDToken(t, "chatgpt-account-vault", "team")
	secretServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/v1/secrets/vault-token" {
			t.Fatalf("secret path = %q", r.URL.Path)
		}
		_ = json.NewEncoder(w).Encode(map[string]string{"value": idToken})
	}))
	t.Cleanup(secretServer.Close)
	t.Setenv("CCP_SECRET_BROKER_URL", secretServer.URL)
	t.Setenv("CCP_SECRET_BROKER_TOKEN", "test-secret-token")

	manager := coreauth.NewManager(nil, nil, nil)
	if _, errRegister := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "codex-vault",
		FileName: "codex-vault.json",
		Provider: "codex",
		Attributes: map[string]string{
			"runtime_only": "true",
		},
		Metadata: map[string]any{
			"id_token": "vault://vault-token",
		},
	}); errRegister != nil {
		t.Fatalf("register auth: %v", errRegister)
	}

	entry := listSingleAuthFile(t, manager)

	if got := entry["chatgpt_account_id"]; got != "chatgpt-account-vault" {
		t.Fatalf("chatgpt_account_id = %#v", got)
	}
	if got := entry["plan_type"]; got != "team" {
		t.Fatalf("plan_type = %#v", got)
	}
}

func TestListAuthFiles_CodexFallsBackToMetadataAccountID(t *testing.T) {
	t.Setenv("MANAGEMENT_PASSWORD", "")
	gin.SetMode(gin.TestMode)

	manager := coreauth.NewManager(nil, nil, nil)
	if _, errRegister := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "codex-fallback",
		FileName: "codex-fallback.json",
		Provider: "codex",
		Attributes: map[string]string{
			"runtime_only": "true",
		},
		Metadata: map[string]any{
			"id_token":   "not-a-jwt",
			"account_id": "chatgpt-account-fallback",
			"plan_type":  "plus",
		},
	}); errRegister != nil {
		t.Fatalf("register auth: %v", errRegister)
	}

	entry := listSingleAuthFile(t, manager)

	if got := entry["chatgpt_account_id"]; got != "chatgpt-account-fallback" {
		t.Fatalf("chatgpt_account_id = %#v", got)
	}
	if got := entry["plan_type"]; got != "plus" {
		t.Fatalf("plan_type = %#v", got)
	}
	if _, ok := entry["id_token"]; ok {
		t.Fatalf("invalid id_token should not be exposed: %#v", entry["id_token"])
	}
}

func listSingleAuthFile(t *testing.T, manager *coreauth.Manager) map[string]any {
	t.Helper()

	handler := NewHandlerWithoutConfigFilePath(&config.Config{AuthDir: t.TempDir()}, manager)
	rec := httptest.NewRecorder()
	ginCtx, _ := gin.CreateTestContext(rec)
	ginCtx.Request = httptest.NewRequest(http.MethodGet, "/v0/management/auth-files", nil)

	handler.ListAuthFiles(ginCtx)

	if rec.Code != http.StatusOK {
		t.Fatalf("list status = %d, body = %s", rec.Code, rec.Body.String())
	}

	var payload struct {
		Files []map[string]any `json:"files"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &payload); err != nil {
		t.Fatalf("decode response: %v", err)
	}
	if len(payload.Files) != 1 {
		t.Fatalf("files length = %d, body = %s", len(payload.Files), rec.Body.String())
	}
	return payload.Files[0]
}

func makeCodexIDToken(t *testing.T, accountID, planType string) string {
	t.Helper()

	header := map[string]any{"alg": "none", "typ": "JWT"}
	payload := map[string]any{
		"https://api.openai.com/auth": map[string]any{
			"chatgpt_account_id": accountID,
			"chatgpt_plan_type":  planType,
		},
	}
	return encodeJWTPart(t, header) + "." + encodeJWTPart(t, payload) + ".signature"
}

func encodeJWTPart(t *testing.T, payload any) string {
	t.Helper()

	data, err := json.Marshal(payload)
	if err != nil {
		t.Fatalf("marshal JWT payload: %v", err)
	}
	return base64.RawURLEncoding.EncodeToString(data)
}

func toJSON(t *testing.T, payload any) string {
	t.Helper()

	data, err := json.Marshal(payload)
	if err != nil {
		t.Fatalf("marshal payload: %v", err)
	}
	return string(data)
}
