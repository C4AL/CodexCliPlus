package management

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestCallGeminiCLIReturnsErrorBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("read failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusInternalServerError,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	err := callGeminiCLI(context.Background(), client, "loadCodeAssist", nil, nil)
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read error response body") {
		t.Fatalf("callGeminiCLI error = %v, want body read failure", err)
	}
}

func TestCallGeminiCLIReturnsDrainReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("drain failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		return &http.Response{
			StatusCode: http.StatusOK,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	err := callGeminiCLI(context.Background(), client, "loadCodeAssist", nil, nil)
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "drain response body") {
		t.Fatalf("callGeminiCLI error = %v, want drain failure", err)
	}
}

func TestReadGeminiUserInfoEmailParsesEmail(t *testing.T) {
	t.Parallel()

	email, bodyBytes, err := readGeminiUserInfoEmail(strings.NewReader(`{"email":"user@example.com"}`))
	if err != nil {
		t.Fatalf("readGeminiUserInfoEmail returned error: %v", err)
	}
	if email != "user@example.com" {
		t.Fatalf("email = %q, want user@example.com", email)
	}
	if string(bodyBytes) != `{"email":"user@example.com"}` {
		t.Fatalf("body = %q, want original JSON", string(bodyBytes))
	}
}

func TestReadGeminiUserInfoEmailReturnsReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("userinfo failed")
	email, bodyBytes, err := readGeminiUserInfoEmail(errorReadCloser{err: readErr})
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read user info response body") {
		t.Fatalf("readGeminiUserInfoEmail error = %v, want read failure", err)
	}
	if email != "" {
		t.Fatalf("email = %q, want empty", email)
	}
	if bodyBytes != nil {
		t.Fatalf("bodyBytes = %q, want nil", string(bodyBytes))
	}
}

func TestFetchGCPProjectsReturnsErrorBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("project body failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusBadGateway,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	_, err := fetchGCPProjects(context.Background(), client)
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read project list error response body") {
		t.Fatalf("fetchGCPProjects error = %v, want body read failure", err)
	}
}

func TestCheckCloudAPIIsEnabledReturnsStatusBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("service status failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	_, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read service status response body") {
		t.Fatalf("checkCloudAPIIsEnabled error = %v, want status body read failure", err)
	}
}

func TestCheckCloudAPIIsEnabledReturnsStatusBodyCloseFailure(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("service status close failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{"state":"ENABLED"}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	_, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close service status response body") {
		t.Fatalf("checkCloudAPIIsEnabled error = %v, want status close failure", err)
	}
}

func TestCheckCloudAPIIsEnabledReturnsEnableBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("service enable failed")
	calls := 0
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		if calls == 1 {
			if req.Method != http.MethodGet {
				t.Fatalf("request method = %s, want GET", req.Method)
			}
			return &http.Response{
				StatusCode: http.StatusOK,
				Body:       io.NopCloser(strings.NewReader(`{"state":"DISABLED"}`)),
				Header:     make(http.Header),
				Request:    req,
			}, nil
		}
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusInternalServerError,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	_, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read service enable response body") {
		t.Fatalf("checkCloudAPIIsEnabled error = %v, want enable body read failure", err)
	}
	if calls != 2 {
		t.Fatalf("request count = %d, want 2", calls)
	}
}

func TestCheckCloudAPIIsEnabledReturnsEnableBodyCloseFailure(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("service enable close failed")
	calls := 0
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		if calls == 1 {
			if req.Method != http.MethodGet {
				t.Fatalf("request method = %s, want GET", req.Method)
			}
			return &http.Response{
				StatusCode: http.StatusOK,
				Body:       io.NopCloser(strings.NewReader(`{"state":"DISABLED"}`)),
				Header:     make(http.Header),
				Request:    req,
			}, nil
		}
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	_, err := checkCloudAPIIsEnabled(context.Background(), client, "project-id")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close service enable response body") {
		t.Fatalf("checkCloudAPIIsEnabled error = %v, want enable close failure", err)
	}
	if calls != 2 {
		t.Fatalf("request count = %d, want 2", calls)
	}
}

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

type errorReadCloser struct {
	err error
}

func (r errorReadCloser) Read([]byte) (int, error) {
	return 0, r.err
}

func (r errorReadCloser) Close() error {
	return nil
}

var _ io.ReadCloser = errorReadCloser{}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
