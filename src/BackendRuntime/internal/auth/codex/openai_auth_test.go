package codex

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"sync/atomic"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
)

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

func TestExchangeCodeForTokensWithRedirectReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("token response close failed")
	auth := &CodexAuth{
		httpClient: &http.Client{
			Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
				if req.Method != http.MethodPost {
					t.Fatalf("request method = %s, want POST", req.Method)
				}
				return &http.Response{
					StatusCode: http.StatusOK,
					Body: closeErrorReadCloser{
						Reader: strings.NewReader(`{"access_token":"access","refresh_token":"refresh","id_token":"id-token","expires_in":3600}`),
						err:    closeErr,
					},
					Header:  make(http.Header),
					Request: req,
				}, nil
			}),
		},
	}

	bundle, err := auth.ExchangeCodeForTokensWithRedirect(
		context.Background(),
		"auth-code",
		"http://127.0.0.1/callback",
		&PKCECodes{CodeVerifier: "verifier"},
	)
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "failed to close token response") {
		t.Fatalf("ExchangeCodeForTokensWithRedirect error = %v, want close failure", err)
	}
	if bundle != nil {
		t.Fatalf("bundle = %#v, want nil", bundle)
	}
}

func TestRefreshTokensReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("refresh response close failed")
	auth := &CodexAuth{
		httpClient: &http.Client{
			Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
				if req.Method != http.MethodPost {
					t.Fatalf("request method = %s, want POST", req.Method)
				}
				return &http.Response{
					StatusCode: http.StatusOK,
					Body: closeErrorReadCloser{
						Reader: strings.NewReader(`{"access_token":"access","refresh_token":"refresh","id_token":"id-token","expires_in":3600}`),
						err:    closeErr,
					},
					Header:  make(http.Header),
					Request: req,
				}, nil
			}),
		},
	}

	tokenData, err := auth.RefreshTokens(context.Background(), "refresh-token")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "failed to close refresh response") {
		t.Fatalf("RefreshTokens error = %v, want close failure", err)
	}
	if tokenData != nil {
		t.Fatalf("tokenData = %#v, want nil", tokenData)
	}
}

func TestRefreshTokensWithRetry_NonRetryableOnlyAttemptsOnce(t *testing.T) {
	var calls int32
	auth := &CodexAuth{
		httpClient: &http.Client{
			Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
				atomic.AddInt32(&calls, 1)
				return &http.Response{
					StatusCode: http.StatusBadRequest,
					Body:       io.NopCloser(strings.NewReader(`{"error":"invalid_grant","code":"refresh_token_reused"}`)),
					Header:     make(http.Header),
					Request:    req,
				}, nil
			}),
		},
	}

	_, err := auth.RefreshTokensWithRetry(context.Background(), "dummy_refresh_token", 3)
	if err == nil {
		t.Fatalf("expected error for non-retryable refresh failure")
	}
	if !strings.Contains(strings.ToLower(err.Error()), "refresh_token_reused") {
		t.Fatalf("expected refresh_token_reused in error, got: %v", err)
	}
	if got := atomic.LoadInt32(&calls); got != 1 {
		t.Fatalf("expected 1 refresh attempt, got %d", got)
	}
}

func TestNewCodexAuthWithProxyURL_OverrideDirectDisablesProxy(t *testing.T) {
	cfg := &config.Config{SDKConfig: config.SDKConfig{ProxyURL: "http://proxy.example.com:8080"}}
	auth := NewCodexAuthWithProxyURL(cfg, "direct")

	transport, ok := auth.httpClient.Transport.(*http.Transport)
	if !ok || transport == nil {
		t.Fatalf("expected http.Transport, got %T", auth.httpClient.Transport)
	}
	if transport.Proxy != nil {
		t.Fatal("expected direct transport to disable proxy function")
	}
}

func TestNewCodexAuthWithProxyURL_OverrideProxyTakesPrecedence(t *testing.T) {
	cfg := &config.Config{SDKConfig: config.SDKConfig{ProxyURL: "http://global.example.com:8080"}}
	auth := NewCodexAuthWithProxyURL(cfg, "http://override.example.com:8081")

	transport, ok := auth.httpClient.Transport.(*http.Transport)
	if !ok || transport == nil {
		t.Fatalf("expected http.Transport, got %T", auth.httpClient.Transport)
	}
	req, errReq := http.NewRequest(http.MethodGet, "https://example.com", nil)
	if errReq != nil {
		t.Fatalf("new request: %v", errReq)
	}
	proxyURL, errProxy := transport.Proxy(req)
	if errProxy != nil {
		t.Fatalf("proxy func: %v", errProxy)
	}
	if proxyURL == nil || proxyURL.String() != "http://override.example.com:8081" {
		t.Fatalf("proxy URL = %v, want http://override.example.com:8081", proxyURL)
	}
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
