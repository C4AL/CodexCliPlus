package management

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

func TestPatchAuthFileStatus_ClearsRuntimeBlockingStateWhenDisabling(t *testing.T) {
	t.Setenv("MANAGEMENT_PASSWORD", "")
	gin.SetMode(gin.TestMode)

	manager := coreauth.NewManager(nil, nil, nil)
	registerBlockedAuth(t, manager, "codex-disable", false, coreauth.StatusActive)
	handler := NewHandlerWithoutConfigFilePath(&config.Config{AuthDir: t.TempDir()}, manager)

	patchAuthFileStatus(t, handler, "codex-disable.json", true)

	updated, ok := manager.GetByID("codex-disable")
	if !ok {
		t.Fatalf("updated auth not found")
	}
	assertRuntimeBlockingStateCleared(t, updated)
	if !updated.Disabled {
		t.Fatalf("Disabled = false, want true")
	}
	if updated.Status != coreauth.StatusDisabled {
		t.Fatalf("Status = %q, want disabled", updated.Status)
	}
}

func TestPatchAuthFileStatus_ClearsRuntimeBlockingStateWhenEnabling(t *testing.T) {
	t.Setenv("MANAGEMENT_PASSWORD", "")
	gin.SetMode(gin.TestMode)

	manager := coreauth.NewManager(nil, nil, nil)
	registerBlockedAuth(t, manager, "codex-enable", true, coreauth.StatusDisabled)
	handler := NewHandlerWithoutConfigFilePath(&config.Config{AuthDir: t.TempDir()}, manager)

	patchAuthFileStatus(t, handler, "codex-enable.json", false)

	updated, ok := manager.GetByID("codex-enable")
	if !ok {
		t.Fatalf("updated auth not found")
	}
	assertRuntimeBlockingStateCleared(t, updated)
	if updated.Disabled {
		t.Fatalf("Disabled = true, want false")
	}
	if updated.Status != coreauth.StatusActive {
		t.Fatalf("Status = %q, want active", updated.Status)
	}
}

func registerBlockedAuth(t *testing.T, manager *coreauth.Manager, id string, disabled bool, status coreauth.Status) {
	t.Helper()

	nextRetry := time.Now().Add(30 * time.Minute).UTC()
	if _, errRegister := manager.Register(context.Background(), &coreauth.Auth{
		ID:             id,
		FileName:       id + ".json",
		Provider:       "codex",
		Status:         status,
		Disabled:       disabled,
		Unavailable:    true,
		LastError:      &coreauth.Error{Code: "quota", Message: "quota exceeded", HTTPStatus: http.StatusTooManyRequests},
		NextRetryAfter: nextRetry,
		Quota: coreauth.QuotaState{
			Exceeded:      true,
			Reason:        "quota",
			NextRecoverAt: nextRetry,
			BackoffLevel:  3,
		},
		Attributes: map[string]string{
			"runtime_only": "true",
		},
		ModelStates: map[string]*coreauth.ModelState{
			"gpt-5.1-codex": {
				Status:         coreauth.StatusError,
				Unavailable:    true,
				NextRetryAfter: nextRetry,
				LastError:      &coreauth.Error{Code: "model_quota", Message: "model quota exceeded", HTTPStatus: http.StatusTooManyRequests},
				Quota: coreauth.QuotaState{
					Exceeded:      true,
					Reason:        "quota",
					NextRecoverAt: nextRetry,
					BackoffLevel:  2,
				},
			},
		},
	}); errRegister != nil {
		t.Fatalf("register auth: %v", errRegister)
	}
}

func patchAuthFileStatus(t *testing.T, handler *Handler, name string, disabled bool) {
	t.Helper()

	body, err := json.Marshal(map[string]any{
		"name":     name,
		"disabled": disabled,
	})
	if err != nil {
		t.Fatalf("marshal request: %v", err)
	}
	rec := httptest.NewRecorder()
	ginCtx, _ := gin.CreateTestContext(rec)
	ginCtx.Request = httptest.NewRequest(http.MethodPatch, "/v0/management/auth-files/status", bytes.NewReader(body))
	ginCtx.Request.Header.Set("Content-Type", "application/json")

	handler.PatchAuthFileStatus(ginCtx)

	if rec.Code != http.StatusOK {
		t.Fatalf("status patch = %d, body = %s", rec.Code, rec.Body.String())
	}
}

func assertRuntimeBlockingStateCleared(t *testing.T, auth *coreauth.Auth) {
	t.Helper()

	if auth.Unavailable {
		t.Fatalf("Unavailable = true, want false")
	}
	if auth.LastError != nil {
		t.Fatalf("LastError = %#v, want nil", auth.LastError)
	}
	if !auth.NextRetryAfter.IsZero() {
		t.Fatalf("NextRetryAfter = %v, want zero", auth.NextRetryAfter)
	}
	if auth.Quota != (coreauth.QuotaState{}) {
		t.Fatalf("Quota = %#v, want zero", auth.Quota)
	}
	if len(auth.ModelStates) != 0 {
		t.Fatalf("ModelStates length = %d, want zero", len(auth.ModelStates))
	}
}
