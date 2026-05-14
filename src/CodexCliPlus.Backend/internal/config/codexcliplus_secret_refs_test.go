package config

import (
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestProtectWithSourceReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("secret save close failed")
	resolver := &codexCliPlusSecretResolver{
		baseURL: "http://broker.test",
		token:   "token",
		client: &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
			if req.Method != http.MethodPost {
				t.Fatalf("request method = %s, want POST", req.Method)
			}
			if req.URL.String() != "http://broker.test/v1/secrets" {
				t.Fatalf("request URL = %s, want http://broker.test/v1/secrets", req.URL.String())
			}
			if req.Header.Get("Authorization") != "Bearer token" {
				t.Fatalf("authorization header = %q, want bearer token", req.Header.Get("Authorization"))
			}
			return &http.Response{
				StatusCode: http.StatusCreated,
				Status:     "201 Created",
				Body: closeErrorReadCloser{
					Reader: strings.NewReader(`{"uri":"ccp-secret://saved"}`),
					err:    closeErr,
				},
				Header:  make(http.Header),
				Request: req,
			}, nil
		})},
	}

	reference, err := resolver.protectWithSource("api-keys", "plain-secret", "ApiKey", "test")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "secret save response close failed") {
		t.Fatalf("protectWithSource error = %v, want close failure", err)
	}
	if reference != "" {
		t.Fatalf("reference = %q, want empty", reference)
	}
}

func TestResolveReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("secret reveal close failed")
	resolver := &codexCliPlusSecretResolver{
		baseURL: "http://broker.test",
		token:   "token",
		client: &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
			if req.Method != http.MethodGet {
				t.Fatalf("request method = %s, want GET", req.Method)
			}
			if req.URL.String() != "http://broker.test/v1/secrets/secret-id" {
				t.Fatalf("request URL = %s, want http://broker.test/v1/secrets/secret-id", req.URL.String())
			}
			if req.Header.Get("Authorization") != "Bearer token" {
				t.Fatalf("authorization header = %q, want bearer token", req.Header.Get("Authorization"))
			}
			return &http.Response{
				StatusCode: http.StatusOK,
				Status:     "200 OK",
				Body: closeErrorReadCloser{
					Reader: strings.NewReader(`{"value":"plain-secret"}`),
					err:    closeErr,
				},
				Header:  make(http.Header),
				Request: req,
			}, nil
		})},
	}

	value, err := resolver.resolve("api-keys[0]", "ccp-secret://secret-id")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "secret_ref secret-id response close failed") {
		t.Fatalf("resolve error = %v, want close failure", err)
	}
	if value != "" {
		t.Fatalf("value = %q, want empty", value)
	}
}

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
