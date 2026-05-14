package management

import (
	"net/http"
	"net/http/httptest"
	"reflect"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
)

func TestDeleteOpenAICompatNormalizesName(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			OpenAICompatibility: []config.OpenAICompatibility{
				{Name: "provider-a", BaseURL: "https://a.example.com"},
				{Name: "provider-b", BaseURL: "https://b.example.com"},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/openai-compatibility?name=%20provider-a%20", nil)

	h.DeleteOpenAICompat(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	if got := len(h.cfg.OpenAICompatibility); got != 1 {
		t.Fatalf("openai compatibility len = %d, want 1", got)
	}
	if got := h.cfg.OpenAICompatibility[0].Name; got != "provider-b" {
		t.Fatalf("remaining provider = %q, want %q", got, "provider-b")
	}
}

func TestDeleteOpenAICompatRejectsBlankNameWithoutMutating(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	h := &Handler{
		cfg: &config.Config{
			OpenAICompatibility: []config.OpenAICompatibility{
				{Name: "provider-a", BaseURL: "https://a.example.com"},
			},
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/openai-compatibility?name=%20", nil)

	h.DeleteOpenAICompat(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusBadRequest, rec.Body.String())
	}
	if got := len(h.cfg.OpenAICompatibility); got != 1 {
		t.Fatalf("openai compatibility len = %d, want 1", got)
	}
	if got := h.cfg.OpenAICompatibility[0].Name; got != "provider-a" {
		t.Fatalf("remaining provider = %q, want %q", got, "provider-a")
	}
}

func TestDeleteOpenAICompatReturnsNotFoundWhenNameDoesNotMatch(t *testing.T) {
	t.Parallel()
	gin.SetMode(gin.TestMode)

	existing := []config.OpenAICompatibility{
		{Name: "provider-a", BaseURL: "https://a.example.com"},
	}
	h := &Handler{
		cfg: &config.Config{
			OpenAICompatibility: append([]config.OpenAICompatibility(nil), existing...),
		},
		configFilePath: writeTestConfigFile(t),
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodDelete, "/v0/management/openai-compatibility?name=missing", nil)

	h.DeleteOpenAICompat(c)

	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want %d; body=%s", rec.Code, http.StatusNotFound, rec.Body.String())
	}
	if !reflect.DeepEqual(h.cfg.OpenAICompatibility, existing) {
		t.Fatalf("openai compatibility = %#v, want %#v", h.cfg.OpenAICompatibility, existing)
	}
}
