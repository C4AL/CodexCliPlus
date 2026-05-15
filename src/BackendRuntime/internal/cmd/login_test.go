package cmd

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

type loginRoundTripFunc func(*http.Request) (*http.Response, error)

func (f loginRoundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

type loginFailingReadCloser struct {
	err error
}

func (r loginFailingReadCloser) Read([]byte) (int, error) {
	return 0, r.err
}

func (r loginFailingReadCloser) Close() error {
	return nil
}

type loginCloseErrorReadCloser struct {
	io.Reader
	err error
}

func (r loginCloseErrorReadCloser) Close() error {
	return r.err
}

func TestCallGeminiCLIReturnsErrorBodyReadFailure(t *testing.T) {
	readErr := errors.New("broken api error body")
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			return &http.Response{
				StatusCode: http.StatusInternalServerError,
				Header:     make(http.Header),
				Body:       loginFailingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}

	err := callGeminiCLI(context.Background(), client, "loadCodeAssist", nil, nil)
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "read api error response body") {
		t.Fatalf("error = %q, want api read context", err.Error())
	}
}

func TestCallGeminiCLIReturnsDrainReadFailure(t *testing.T) {
	readErr := errors.New("broken success body")
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			return &http.Response{
				StatusCode: http.StatusOK,
				Header:     make(http.Header),
				Body:       loginFailingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}

	err := callGeminiCLI(context.Background(), client, "loadCodeAssist", nil, nil)
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "drain response body") {
		t.Fatalf("error = %q, want drain read context", err.Error())
	}
}

func TestFetchGCPProjectsReturnsErrorBodyReadFailure(t *testing.T) {
	readErr := errors.New("broken project list body")
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			return &http.Response{
				StatusCode: http.StatusInternalServerError,
				Header:     make(http.Header),
				Body:       loginFailingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}

	_, err := fetchGCPProjects(context.Background(), client)
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "read project list error response body") {
		t.Fatalf("error = %q, want project list read context", err.Error())
	}
}

func TestCheckCloudAPIIsEnabledReturnsStatusBodyReadFailure(t *testing.T) {
	readErr := errors.New("broken service status body")
	calls := 0
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			calls++
			return &http.Response{
				StatusCode: http.StatusOK,
				Header:     make(http.Header),
				Body:       loginFailingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}

	enabled, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if enabled {
		t.Fatal("enabled = true, want false")
	}
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "read service status response body") {
		t.Fatalf("error = %q, want service status read context", err.Error())
	}
	if calls != 1 {
		t.Fatalf("request count = %d, want 1", calls)
	}
}

func TestCheckCloudAPIIsEnabledReturnsStatusBodyCloseFailure(t *testing.T) {
	closeErr := errors.New("broken service status close")
	calls := 0
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			calls++
			return &http.Response{
				StatusCode: http.StatusOK,
				Header:     make(http.Header),
				Body: loginCloseErrorReadCloser{
					Reader: strings.NewReader(`{"state":"ENABLED"}`),
					err:    closeErr,
				},
				Request: req,
			}, nil
		}),
	}

	enabled, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if enabled {
		t.Fatal("enabled = true, want false")
	}
	if !errors.Is(err, closeErr) {
		t.Fatalf("error = %v, want wrapped close error", err)
	}
	if !strings.Contains(err.Error(), "close service status response body") {
		t.Fatalf("error = %q, want service status close context", err.Error())
	}
	if calls != 1 {
		t.Fatalf("request count = %d, want 1", calls)
	}
}

func TestCheckCloudAPIIsEnabledReturnsEnableBodyReadFailure(t *testing.T) {
	readErr := errors.New("broken service enable body")
	calls := 0
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			calls++
			if calls == 1 {
				return &http.Response{
					StatusCode: http.StatusNotFound,
					Header:     make(http.Header),
					Body:       io.NopCloser(strings.NewReader("{}")),
					Request:    req,
				}, nil
			}
			return &http.Response{
				StatusCode: http.StatusBadRequest,
				Header:     make(http.Header),
				Body:       loginFailingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}

	enabled, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if enabled {
		t.Fatal("enabled = true, want false")
	}
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "read service enable response body") {
		t.Fatalf("error = %q, want service enable read context", err.Error())
	}
	if calls != 2 {
		t.Fatalf("request count = %d, want 2", calls)
	}
}

func TestCheckCloudAPIIsEnabledReturnsEnableBodyCloseFailure(t *testing.T) {
	closeErr := errors.New("broken service enable close")
	calls := 0
	client := &http.Client{
		Transport: loginRoundTripFunc(func(req *http.Request) (*http.Response, error) {
			calls++
			if calls == 1 {
				return &http.Response{
					StatusCode: http.StatusOK,
					Header:     make(http.Header),
					Body:       io.NopCloser(strings.NewReader(`{"state":"DISABLED"}`)),
					Request:    req,
				}, nil
			}
			return &http.Response{
				StatusCode: http.StatusOK,
				Header:     make(http.Header),
				Body: loginCloseErrorReadCloser{
					Reader: strings.NewReader(`{}`),
					err:    closeErr,
				},
				Request: req,
			}, nil
		}),
	}

	enabled, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if enabled {
		t.Fatal("enabled = true, want false")
	}
	if !errors.Is(err, closeErr) {
		t.Fatalf("error = %v, want wrapped close error", err)
	}
	if !strings.Contains(err.Error(), "close service enable response body") {
		t.Fatalf("error = %q, want service enable close context", err.Error())
	}
	if calls != 2 {
		t.Fatalf("request count = %d, want 2", calls)
	}
}
