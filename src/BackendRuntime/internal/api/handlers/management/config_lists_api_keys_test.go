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

func TestPutAPIKeysNormalizesDistinctValues(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg:            &config.Config{},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPut,
		"/v0/management/api-keys",
		strings.NewReader(`[" key-a ","key-b","key-a"," "]`),
	)

	h.PutAPIKeys(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	want := []string{"key-a", "key-b"}
	if !reflect.DeepEqual(h.cfg.APIKeys, want) {
		t.Fatalf("api keys = %#v, want %#v", h.cfg.APIKeys, want)
	}
}

func TestPatchAPIKeysAppendsWhenOldIsNullAndNormalizesNew(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			SDKConfig: config.SDKConfig{APIKeys: []string{"key-a"}},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPatch,
		"/v0/management/api-keys",
		strings.NewReader(`{"old":null,"new":" key-b "}`),
	)

	h.PatchAPIKeys(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	want := []string{"key-a", "key-b"}
	if !reflect.DeepEqual(h.cfg.APIKeys, want) {
		t.Fatalf("api keys = %#v, want %#v", h.cfg.APIKeys, want)
	}
}

func TestPatchAPIKeysRejectsBlankValuesWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			SDKConfig: config.SDKConfig{APIKeys: []string{"key-a"}},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPatch,
		"/v0/management/api-keys",
		strings.NewReader(`{"index":0,"value":" "}`),
	)

	h.PatchAPIKeys(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	want := []string{"key-a"}
	if !reflect.DeepEqual(h.cfg.APIKeys, want) {
		t.Fatalf("api keys = %#v, want %#v", h.cfg.APIKeys, want)
	}
}

func TestDeleteAPIKeysByValueReturnsNotFoundWithoutMutating(t *testing.T) {
	t.Parallel()
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
	c.Request = httptest.NewRequest(
		http.MethodDelete,
		"/v0/management/api-keys?value=missing",
		nil,
	)

	h.DeleteAPIKeys(c)

	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusNotFound, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.APIKeys, existing) {
		t.Fatalf("api keys = %#v, want %#v", h.cfg.APIKeys, existing)
	}
}
