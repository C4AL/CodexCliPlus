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

func TestPutAmpModelMappingsNormalizesAndFiltersInvalidValues(t *testing.T) {
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
		"/v0/management/ampcode/model-mappings",
		strings.NewReader(`{"value":[{"from":" source-model ","to":" target-model "},{"from":" ","to":"ignored"},{"from":"missing-target","to":" "}]}`),
	)

	h.PutAmpModelMappings(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	want := []config.AmpModelMapping{{From: "source-model", To: "target-model"}}
	if !reflect.DeepEqual(h.cfg.AmpCode.ModelMappings, want) {
		t.Fatalf("model mappings = %#v, want %#v", h.cfg.AmpCode.ModelMappings, want)
	}
}

func TestPutAmpModelMappingsRejectsMissingValueWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{
				ModelMappings: []config.AmpModelMapping{{From: "source-model", To: "target-model"}},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPut, "/v0/management/ampcode/model-mappings", strings.NewReader(`{}`))

	h.PutAmpModelMappings(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	want := []config.AmpModelMapping{{From: "source-model", To: "target-model"}}
	if !reflect.DeepEqual(h.cfg.AmpCode.ModelMappings, want) {
		t.Fatalf("model mappings = %#v, want %#v", h.cfg.AmpCode.ModelMappings, want)
	}
}

func TestPatchAmpModelMappingsNormalizesAndSkipsInvalidValues(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{
				ModelMappings: []config.AmpModelMapping{{From: "source-model", To: "old-target"}},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(
		http.MethodPatch,
		"/v0/management/ampcode/model-mappings",
		strings.NewReader(`{"value":[{"from":" source-model ","to":" target-model "},{"from":" ","to":"ignored"}]}`),
	)

	h.PatchAmpModelMappings(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	want := []config.AmpModelMapping{{From: "source-model", To: "target-model"}}
	if !reflect.DeepEqual(h.cfg.AmpCode.ModelMappings, want) {
		t.Fatalf("model mappings = %#v, want %#v", h.cfg.AmpCode.ModelMappings, want)
	}
}

func TestPatchAmpModelMappingsRejectsMissingValueWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{
				ModelMappings: []config.AmpModelMapping{{From: "source-model", To: "target-model"}},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/v0/management/ampcode/model-mappings", strings.NewReader(`{}`))

	h.PatchAmpModelMappings(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	want := []config.AmpModelMapping{{From: "source-model", To: "target-model"}}
	if !reflect.DeepEqual(h.cfg.AmpCode.ModelMappings, want) {
		t.Fatalf("model mappings = %#v, want %#v", h.cfg.AmpCode.ModelMappings, want)
	}
}

func TestDeleteAmpModelMappingsRejectsInvalidBodyWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	tests := []struct {
		name string
		body string
	}{
		{name: "invalid json", body: `{`},
		{name: "missing value", body: `{}`},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			t.Parallel()

			h := &Handler{
				cfg: &config.Config{
					AmpCode: config.AmpCode{
						ModelMappings: []config.AmpModelMapping{
							{From: "source-model", To: "target-model"},
						},
					},
				},
				configFilePath: writeTestConfigFile(t),
			}

			rec := httptest.NewRecorder()
			c, _ := gin.CreateTestContext(rec)
			c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/ampcode/model-mappings", strings.NewReader(tt.body))

			h.DeleteAmpModelMappings(c)

			if rec.Code != http.StatusBadRequest {
				t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
			}
			if got := len(h.cfg.AmpCode.ModelMappings); got != 1 {
				t.Fatalf("model mappings len = %d, want 1", got)
			}
			if got := h.cfg.AmpCode.ModelMappings[0].From; got != "source-model" {
				t.Fatalf("remaining mapping from = %q, want %q", got, "source-model")
			}
		})
	}
}

func TestDeleteAmpModelMappingsEmptyValueClearsAll(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{
				ModelMappings: []config.AmpModelMapping{
					{From: "source-model", To: "target-model"},
				},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/ampcode/model-mappings", strings.NewReader(`{"value":[]}`))

	h.DeleteAmpModelMappings(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	if got := len(h.cfg.AmpCode.ModelMappings); got != 0 {
		t.Fatalf("model mappings len = %d, want 0", got)
	}
}

func TestDeleteAmpModelMappingsReturnsNotFoundWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := []config.AmpModelMapping{
		{From: "source-model", To: "target-model"},
	}
	h := &Handler{
		cfg: &config.Config{
			AmpCode: config.AmpCode{
				ModelMappings: append([]config.AmpModelMapping(nil), existing...),
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/ampcode/model-mappings", strings.NewReader(`{"value":["missing-model"]}`))

	h.DeleteAmpModelMappings(c)

	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusNotFound, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.AmpCode.ModelMappings, existing) {
		t.Fatalf("model mappings = %#v, want %#v", h.cfg.AmpCode.ModelMappings, existing)
	}
}
