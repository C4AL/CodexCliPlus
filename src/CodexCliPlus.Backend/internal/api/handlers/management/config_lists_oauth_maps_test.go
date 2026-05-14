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

func TestPatchOAuthExcludedModelsRejectsMissingModelsWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := map[string][]string{"codex": {"gpt-5"}}
	h := &Handler{
		cfg: &config.Config{
			OAuthExcludedModels: map[string][]string{"codex": {"gpt-5"}},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/v0/management/oauth-excluded-models", strings.NewReader(`{"provider":"codex"}`))

	h.PatchOAuthExcludedModels(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.OAuthExcludedModels, existing) {
		t.Fatalf("oauth excluded models = %#v, want %#v", h.cfg.OAuthExcludedModels, existing)
	}
}

func TestPatchOAuthModelAliasRejectsMissingAliasesWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := map[string][]config.OAuthModelAlias{
		"codex": {{Name: "gpt-5", Alias: "gpt-5-chat"}},
	}
	h := &Handler{
		cfg: &config.Config{
			OAuthModelAlias: map[string][]config.OAuthModelAlias{
				"codex": {{Name: "gpt-5", Alias: "gpt-5-chat"}},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/v0/management/oauth-model-alias", strings.NewReader(`{"channel":"codex"}`))

	h.PatchOAuthModelAlias(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.OAuthModelAlias, existing) {
		t.Fatalf("oauth model alias = %#v, want %#v", h.cfg.OAuthModelAlias, existing)
	}
}
