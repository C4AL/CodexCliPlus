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

func TestPutAmpUpstreamAPIKeysRejectsMissingValueWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := []config.AmpUpstreamAPIKeyEntry{
		{UpstreamAPIKey: "upstream-a", APIKeys: []string{"client-a"}},
	}
	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{UpstreamAPIKeys: append([]config.AmpUpstreamAPIKeyEntry(nil), existing...)},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPut, "/v0/management/ampcode/upstream-api-keys", strings.NewReader(`{}`))

	h.PutAmpUpstreamAPIKeys(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.AmpCode.UpstreamAPIKeys, existing) {
		t.Fatalf("upstream api keys = %#v, want %#v", h.cfg.AmpCode.UpstreamAPIKeys, existing)
	}
}

func TestPatchAmpUpstreamAPIKeysRejectsMissingValueWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := []config.AmpUpstreamAPIKeyEntry{
		{UpstreamAPIKey: "upstream-a", APIKeys: []string{"client-a"}},
	}
	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{UpstreamAPIKeys: append([]config.AmpUpstreamAPIKeyEntry(nil), existing...)},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/v0/management/ampcode/upstream-api-keys", strings.NewReader(`{}`))

	h.PatchAmpUpstreamAPIKeys(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.AmpCode.UpstreamAPIKeys, existing) {
		t.Fatalf("upstream api keys = %#v, want %#v", h.cfg.AmpCode.UpstreamAPIKeys, existing)
	}
}

func TestDeleteAmpUpstreamAPIKeysReturnsNotFoundWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := []config.AmpUpstreamAPIKeyEntry{
		{UpstreamAPIKey: "upstream-a", APIKeys: []string{"client-a"}},
	}
	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{UpstreamAPIKeys: append([]config.AmpUpstreamAPIKeyEntry(nil), existing...)},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/ampcode/upstream-api-keys", strings.NewReader(`{"value":["missing-upstream"]}`))

	h.DeleteAmpUpstreamAPIKeys(c)

	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusNotFound, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.AmpCode.UpstreamAPIKeys, existing) {
		t.Fatalf("upstream api keys = %#v, want %#v", h.cfg.AmpCode.UpstreamAPIKeys, existing)
	}
}
