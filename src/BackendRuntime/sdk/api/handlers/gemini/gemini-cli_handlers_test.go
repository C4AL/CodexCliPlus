package gemini

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/api/handlers"
)

type errorReadCloser struct {
	err error
}

func (r errorReadCloser) Read([]byte) (int, error) {
	return 0, r.err
}

func (r errorReadCloser) Close() error {
	return nil
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}

func TestWriteGeminiCLISuccessResponseSkipsEmptyHeaderValues(t *testing.T) {
	gin.SetMode(gin.TestMode)
	recorder := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(recorder)

	writeGeminiCLISuccessResponse(c, &http.Response{
		StatusCode: http.StatusOK,
		Header: http.Header{
			"X-Empty": nil,
			"X-Trace": {"trace-id"},
		},
		Body: io.NopCloser(strings.NewReader("ok")),
	})

	if recorder.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusOK)
	}
	if got := recorder.Body.String(); got != "ok" {
		t.Fatalf("body = %q, want ok", got)
	}
	if values := recorder.Header().Values("X-Empty"); len(values) != 0 {
		t.Fatalf("X-Empty values = %#v, want none", values)
	}
	if got := recorder.Header().Get("X-Trace"); got != "trace-id" {
		t.Fatalf("X-Trace = %q, want trace-id", got)
	}
}

func TestWriteGeminiCLISuccessResponseReportsCloseFailure(t *testing.T) {
	gin.SetMode(gin.TestMode)
	recorder := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(recorder)

	writeGeminiCLISuccessResponse(c, &http.Response{
		StatusCode: http.StatusOK,
		Body: closeErrorReadCloser{
			Reader: strings.NewReader("ok"),
			err:    errors.New("upstream close failed"),
		},
	})

	if recorder.Code != http.StatusBadGateway {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusBadGateway)
	}

	var body handlers.ErrorResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("response body is not valid JSON: %v", err)
	}
	if body.Error.Type != "server_error" {
		t.Fatalf("error type = %q, want server_error", body.Error.Type)
	}
	if !strings.Contains(body.Error.Message, "upstream close failed") {
		t.Fatalf("error message = %q, want close failure detail", body.Error.Message)
	}
}

func TestWriteGeminiCLIUpstreamErrorResponseReportsReadFailure(t *testing.T) {
	gin.SetMode(gin.TestMode)
	recorder := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(recorder)

	writeGeminiCLIUpstreamErrorResponse(c, &http.Response{
		StatusCode: http.StatusTooManyRequests,
		Body:       errorReadCloser{err: errors.New("broken upstream body")},
	})

	if recorder.Code != http.StatusBadGateway {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusBadGateway)
	}

	var body handlers.ErrorResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("response body is not valid JSON: %v", err)
	}
	if body.Error.Type != "server_error" {
		t.Fatalf("error type = %q, want server_error", body.Error.Type)
	}
	if !strings.Contains(body.Error.Message, "broken upstream body") {
		t.Fatalf("error message = %q, want read failure detail", body.Error.Message)
	}
}

func TestWriteGeminiCLIUpstreamErrorResponseKeepsUpstreamBody(t *testing.T) {
	gin.SetMode(gin.TestMode)
	recorder := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(recorder)

	writeGeminiCLIUpstreamErrorResponse(c, &http.Response{
		StatusCode: http.StatusTooManyRequests,
		Body:       io.NopCloser(strings.NewReader("quota exceeded")),
	})

	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want %d", recorder.Code, http.StatusBadRequest)
	}

	var body handlers.ErrorResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("response body is not valid JSON: %v", err)
	}
	if body.Error.Type != "invalid_request_error" {
		t.Fatalf("error type = %q, want invalid_request_error", body.Error.Type)
	}
	if body.Error.Message != "quota exceeded" {
		t.Fatalf("error message = %q, want quota exceeded", body.Error.Message)
	}
}
