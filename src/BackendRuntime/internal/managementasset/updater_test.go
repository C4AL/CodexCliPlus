package managementasset

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestFetchLatestAssetReturnsErrorBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("release body failed")
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

	asset, digest, err := fetchLatestAsset(context.Background(), client, "https://example.test/releases/latest")
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read release error response body") {
		t.Fatalf("fetchLatestAsset error = %v, want body read failure", err)
	}
	if asset != nil {
		t.Fatalf("asset = %#v, want nil", asset)
	}
	if digest != "" {
		t.Fatalf("digest = %q, want empty", digest)
	}
}

func TestFetchLatestAssetReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("release body close failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{"assets":[{"name":"management.html","browser_download_url":"https://example.test/management.html","digest":"sha256:abc123"}]}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	asset, digest, err := fetchLatestAsset(context.Background(), client, "https://example.test/releases/latest")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close release response body") {
		t.Fatalf("fetchLatestAsset error = %v, want close failure", err)
	}
	if asset != nil {
		t.Fatalf("asset = %#v, want nil", asset)
	}
	if digest != "" {
		t.Fatalf("digest = %q, want empty", digest)
	}
}

func TestDownloadAssetReturnsErrorBodyReadFailure(t *testing.T) {
	t.Parallel()

	readErr := errors.New("download body failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusInternalServerError,
			Body:       errorReadCloser{err: readErr},
			Header:     make(http.Header),
			Request:    req,
		}, nil
	})}

	data, digest, err := downloadAsset(context.Background(), client, "https://example.test/management.html")
	if !errors.Is(err, readErr) || !strings.Contains(err.Error(), "read download error response body") {
		t.Fatalf("downloadAsset error = %v, want body read failure", err)
	}
	if data != nil {
		t.Fatalf("data = %q, want nil", string(data))
	}
	if digest != "" {
		t.Fatalf("digest = %q, want empty", digest)
	}
}

func TestDownloadAssetReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("download body close failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodGet {
			t.Fatalf("request method = %s, want GET", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader("management asset"),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	data, digest, err := downloadAsset(context.Background(), client, "https://example.test/management.html")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close download response body") {
		t.Fatalf("downloadAsset error = %v, want close failure", err)
	}
	if data != nil {
		t.Fatalf("data = %q, want nil", string(data))
	}
	if digest != "" {
		t.Fatalf("digest = %q, want empty", digest)
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
