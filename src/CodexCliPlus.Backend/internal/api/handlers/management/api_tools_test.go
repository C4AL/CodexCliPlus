package management

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	sdkconfig "github.com/router-for-me/CLIProxyAPI/v7/sdk/config"
	"golang.org/x/oauth2"
)

func TestAPICallTransportDirectBypassesGlobalProxy(t *testing.T) {
	t.Parallel()

	h := &Handler{
		cfg: &config.Config{
			SDKConfig: sdkconfig.SDKConfig{ProxyURL: "http://global-proxy.example.com:8080"},
		},
	}

	transport := h.apiCallTransport(&coreauth.Auth{ProxyURL: "direct"})
	httpTransport, ok := transport.(*http.Transport)
	if !ok {
		t.Fatalf("transport type = %T, want *http.Transport", transport)
	}
	if httpTransport.Proxy != nil {
		t.Fatal("expected direct transport to disable proxy function")
	}
}

func TestAPICallTransportInvalidAuthFallsBackToGlobalProxy(t *testing.T) {
	t.Parallel()

	h := &Handler{
		cfg: &config.Config{
			SDKConfig: sdkconfig.SDKConfig{ProxyURL: "http://global-proxy.example.com:8080"},
		},
	}

	transport := h.apiCallTransport(&coreauth.Auth{ProxyURL: "bad-value"})
	httpTransport, ok := transport.(*http.Transport)
	if !ok {
		t.Fatalf("transport type = %T, want *http.Transport", transport)
	}

	req, errRequest := http.NewRequest(http.MethodGet, "https://example.com", nil)
	if errRequest != nil {
		t.Fatalf("http.NewRequest returned error: %v", errRequest)
	}

	proxyURL, errProxy := httpTransport.Proxy(req)
	if errProxy != nil {
		t.Fatalf("httpTransport.Proxy returned error: %v", errProxy)
	}
	if proxyURL == nil || proxyURL.String() != "http://global-proxy.example.com:8080" {
		t.Fatalf("proxy URL = %v, want http://global-proxy.example.com:8080", proxyURL)
	}
}

func TestAPICallTransportAPIKeyAuthFallsBackToConfigProxyURL(t *testing.T) {
	t.Parallel()

	h := &Handler{
		cfg: &config.Config{
			SDKConfig: sdkconfig.SDKConfig{ProxyURL: "http://global-proxy.example.com:8080"},
			GeminiKey: []config.GeminiKey{{
				APIKey:   "gemini-key",
				ProxyURL: "http://gemini-proxy.example.com:8080",
			}},
			ClaudeKey: []config.ClaudeKey{{
				APIKey:   "claude-key",
				ProxyURL: "http://claude-proxy.example.com:8080",
			}},
			CodexKey: []config.CodexKey{{
				APIKey:   "codex-key",
				ProxyURL: "http://codex-proxy.example.com:8080",
			}},
			OpenAICompatibility: []config.OpenAICompatibility{{
				Name:    "bohe",
				BaseURL: "https://bohe.example.com",
				APIKeyEntries: []config.OpenAICompatibilityAPIKey{{
					APIKey:   "compat-key",
					ProxyURL: "http://compat-proxy.example.com:8080",
				}},
			}},
		},
	}

	cases := []struct {
		name      string
		auth      *coreauth.Auth
		wantProxy string
	}{
		{
			name: "gemini",
			auth: &coreauth.Auth{
				Provider:   "gemini",
				Attributes: map[string]string{"api_key": "gemini-key"},
			},
			wantProxy: "http://gemini-proxy.example.com:8080",
		},
		{
			name: "claude",
			auth: &coreauth.Auth{
				Provider:   "claude",
				Attributes: map[string]string{"api_key": "claude-key"},
			},
			wantProxy: "http://claude-proxy.example.com:8080",
		},
		{
			name: "codex",
			auth: &coreauth.Auth{
				Provider:   "codex",
				Attributes: map[string]string{"api_key": "codex-key"},
			},
			wantProxy: "http://codex-proxy.example.com:8080",
		},
		{
			name: "openai-compatibility",
			auth: &coreauth.Auth{
				Provider: "bohe",
				Attributes: map[string]string{
					"api_key":      "compat-key",
					"compat_name":  "bohe",
					"provider_key": "bohe",
				},
			},
			wantProxy: "http://compat-proxy.example.com:8080",
		},
	}

	for _, tc := range cases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			transport := h.apiCallTransport(tc.auth)
			httpTransport, ok := transport.(*http.Transport)
			if !ok {
				t.Fatalf("transport type = %T, want *http.Transport", transport)
			}

			req, errRequest := http.NewRequest(http.MethodGet, "https://example.com", nil)
			if errRequest != nil {
				t.Fatalf("http.NewRequest returned error: %v", errRequest)
			}

			proxyURL, errProxy := httpTransport.Proxy(req)
			if errProxy != nil {
				t.Fatalf("httpTransport.Proxy returned error: %v", errProxy)
			}
			if proxyURL == nil || proxyURL.String() != tc.wantProxy {
				t.Fatalf("proxy URL = %v, want %s", proxyURL, tc.wantProxy)
			}
		})
	}
}

func TestAuthByIndexDistinguishesSharedAPIKeysAcrossProviders(t *testing.T) {
	t.Parallel()

	manager := coreauth.NewManager(nil, nil, nil)
	geminiAuth := &coreauth.Auth{
		ID:       "gemini:apikey:123",
		Provider: "gemini",
		Attributes: map[string]string{
			"api_key": "shared-key",
		},
	}
	compatAuth := &coreauth.Auth{
		ID:       "openai-compatibility:bohe:456",
		Provider: "bohe",
		Label:    "bohe",
		Attributes: map[string]string{
			"api_key":      "shared-key",
			"compat_name":  "bohe",
			"provider_key": "bohe",
		},
	}

	if _, errRegister := manager.Register(context.Background(), geminiAuth); errRegister != nil {
		t.Fatalf("register gemini auth: %v", errRegister)
	}
	if _, errRegister := manager.Register(context.Background(), compatAuth); errRegister != nil {
		t.Fatalf("register compat auth: %v", errRegister)
	}

	geminiIndex := geminiAuth.EnsureIndex()
	compatIndex := compatAuth.EnsureIndex()
	if geminiIndex == compatIndex {
		t.Fatalf("shared api key produced duplicate auth_index %q", geminiIndex)
	}

	h := &Handler{authManager: manager}

	gotGemini := h.authByIndex(geminiIndex)
	if gotGemini == nil {
		t.Fatal("expected gemini auth by index")
	}
	if gotGemini.ID != geminiAuth.ID {
		t.Fatalf("authByIndex(gemini) returned %q, want %q", gotGemini.ID, geminiAuth.ID)
	}

	gotCompat := h.authByIndex(compatIndex)
	if gotCompat == nil {
		t.Fatal("expected compat auth by index")
	}
	if gotCompat.ID != compatAuth.ID {
		t.Fatalf("authByIndex(compat) returned %q, want %q", gotCompat.ID, compatAuth.ID)
	}
}

func TestBuildOAuthTokenMapMatchesTokenJSONFields(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name string
		tok  *oauth2.Token
	}{
		{
			name: "full token",
			tok: &oauth2.Token{
				AccessToken:  "new-token",
				TokenType:    "Bearer",
				RefreshToken: "refresh-token",
				Expiry:       time.Date(2026, 1, 2, 3, 4, 5, 123456789, time.UTC),
				ExpiresIn:    3600,
			},
		},
		{
			name: "zero expiry token",
			tok: &oauth2.Token{
				AccessToken: "new-token",
			},
		},
	}

	for _, tc := range cases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			base := map[string]any{
				"access_token":  "old-token",
				"custom":        "kept",
				"expires_in":    float64(12),
				"refresh_token": "old-refresh",
				"token_type":    "old-type",
			}
			got := buildOAuthTokenMap(base, tc.tok)

			want := cloneMap(base)
			raw, errMarshal := json.Marshal(tc.tok)
			if errMarshal != nil {
				t.Fatalf("json.Marshal token returned error: %v", errMarshal)
			}
			var tokenMap map[string]any
			if errUnmarshal := json.Unmarshal(raw, &tokenMap); errUnmarshal != nil {
				t.Fatalf("json.Unmarshal token returned error: %v", errUnmarshal)
			}
			for k, v := range tokenMap {
				want[k] = v
			}

			if !reflect.DeepEqual(got, want) {
				t.Fatalf("token map = %#v, want %#v", got, want)
			}
			if base["access_token"] != "old-token" {
				t.Fatalf("base map was mutated: %#v", base)
			}
		})
	}
}

func TestOAuthTokenFromMapParsesKnownFields(t *testing.T) {
	t.Parallel()

	expiry := time.Date(2026, 2, 3, 4, 5, 6, 123456789, time.UTC)
	got := oauthTokenFromMap(map[string]any{
		"access_token":  "access-token",
		"token_type":    "Bearer",
		"refresh_token": "refresh-token",
		"expiry":        expiry.Format(time.RFC3339Nano),
		"expires_in":    float64(3600),
	})

	if got.AccessToken != "access-token" {
		t.Fatalf("AccessToken = %q, want access-token", got.AccessToken)
	}
	if got.TokenType != "Bearer" {
		t.Fatalf("TokenType = %q, want Bearer", got.TokenType)
	}
	if got.RefreshToken != "refresh-token" {
		t.Fatalf("RefreshToken = %q, want refresh-token", got.RefreshToken)
	}
	if !got.Expiry.Equal(expiry) {
		t.Fatalf("Expiry = %s, want %s", got.Expiry.Format(time.RFC3339Nano), expiry.Format(time.RFC3339Nano))
	}
	if got.ExpiresIn != 3600 {
		t.Fatalf("ExpiresIn = %d, want 3600", got.ExpiresIn)
	}
}

func TestOAuthTokenFromMapKeepsFieldsWhenExpiryIsInvalid(t *testing.T) {
	t.Parallel()

	got := oauthTokenFromMap(map[string]any{
		"access_token":  "access-token",
		"refresh_token": "refresh-token",
		"expiry":        "not-a-time",
	})

	if got.AccessToken != "access-token" {
		t.Fatalf("AccessToken = %q, want access-token", got.AccessToken)
	}
	if got.RefreshToken != "refresh-token" {
		t.Fatalf("RefreshToken = %q, want refresh-token", got.RefreshToken)
	}
	if !got.Expiry.IsZero() {
		t.Fatalf("Expiry = %s, want zero value", got.Expiry.Format(time.RFC3339Nano))
	}
}

func TestRefreshAntigravityOAuthAccessTokenReturnsPersistError(t *testing.T) {
	tokenServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if err := r.ParseForm(); err != nil {
			t.Fatalf("failed to parse token request form: %v", err)
		}
		if got := r.Form.Get("refresh_token"); got != "refresh-token" {
			t.Fatalf("refresh_token = %q, want refresh-token", got)
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"access_token":"new-token","expires_in":3600,"token_type":"Bearer"}`))
	}))
	defer tokenServer.Close()

	originalTokenURL := antigravityOAuthTokenURL
	antigravityOAuthTokenURL = tokenServer.URL
	t.Cleanup(func() { antigravityOAuthTokenURL = originalTokenURL })

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	auth := &coreauth.Auth{
		ID:       "antigravity-auth",
		Provider: "antigravity",
		Metadata: map[string]any{
			"access_token":  "old-token",
			"refresh_token": "refresh-token",
			"expired":       time.Now().Add(-time.Minute).Format(time.RFC3339),
			"type":          "antigravity",
		},
	}
	if _, errRegister := manager.Register(context.Background(), auth); errRegister != nil {
		t.Fatalf("register auth: %v", errRegister)
	}
	store.failSaves("refresh save failed")

	h := &Handler{authManager: manager}
	_, err := h.refreshAntigravityOAuthAccessToken(context.Background(), auth)
	if err == nil || !strings.Contains(err.Error(), "refresh save failed") {
		t.Fatalf("refresh error = %v, want persist failure", err)
	}
}
