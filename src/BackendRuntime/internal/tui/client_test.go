package tui

import (
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

func TestDoRequestReturnsCloseFailure(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("close failed")
	client := &Client{
		baseURL: "http://example.test",
		http: &http.Client{
			Transport: roundTripFunc(func(*http.Request) (*http.Response, error) {
				return &http.Response{
					StatusCode: http.StatusOK,
					Body:       closeErrorReadCloser{Reader: strings.NewReader(`{"status":"ok"}`), err: closeErr},
				}, nil
			}),
		},
	}

	data, status, err := client.doRequest(http.MethodGet, "/v0/management/status", nil)
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close response body") {
		t.Fatalf("doRequest error = %v, want close failure", err)
	}
	if status != http.StatusOK {
		t.Fatalf("status = %d, want %d", status, http.StatusOK)
	}
	if data != nil {
		t.Fatalf("data = %q, want nil", string(data))
	}
}

func TestGetLogsParsesLinesAndLatestTimestamp(t *testing.T) {
	t.Parallel()

	var gotPath string
	client := &Client{
		baseURL: "http://example.test",
		http: &http.Client{
			Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
				gotPath = req.URL.RequestURI()
				return &http.Response{
					StatusCode: http.StatusOK,
					Body:       io.NopCloser(strings.NewReader(`{"lines":["first","second"],"latest-timestamp":7}`)),
				}, nil
			}),
		},
	}

	lines, latest, err := client.GetLogs(5, 10)
	if err != nil {
		t.Fatalf("GetLogs() error = %v", err)
	}
	if gotPath != "/v0/management/logs?after=5&limit=10" {
		t.Fatalf("request URI = %q, want %q", gotPath, "/v0/management/logs?after=5&limit=10")
	}
	if latest != 7 {
		t.Fatalf("latest = %d, want 7", latest)
	}
	if len(lines) != 2 || lines[0] != "first" || lines[1] != "second" {
		t.Fatalf("lines = %#v, want first/second", lines)
	}
}

func TestGetLogsRejectsNonStringLines(t *testing.T) {
	t.Parallel()

	client := &Client{
		baseURL: "http://example.test",
		http: &http.Client{
			Transport: roundTripFunc(func(*http.Request) (*http.Response, error) {
				return &http.Response{
					StatusCode: http.StatusOK,
					Body:       io.NopCloser(strings.NewReader(`{"lines":[3],"latest-timestamp":7}`)),
				}, nil
			}),
		},
	}

	if _, _, err := client.GetLogs(5, 10); err == nil {
		t.Fatal("expected error")
	}
}

func TestPatchAuthFileFieldsReturnsMarshalErrorWithoutRequest(t *testing.T) {
	t.Parallel()

	requests := 0
	client := &Client{
		baseURL: "http://example.test",
		http: &http.Client{
			Transport: roundTripFunc(func(*http.Request) (*http.Response, error) {
				requests++
				return &http.Response{
					StatusCode: http.StatusOK,
					Body:       io.NopCloser(strings.NewReader(`{"status":"ok"}`)),
				}, nil
			}),
		},
	}

	err := client.PatchAuthFileFields("auth.json", map[string]any{
		"metadata": func() {},
	})

	if err == nil {
		t.Fatal("expected marshal error")
	}
	if requests != 0 {
		t.Fatalf("requests = %d, want 0", requests)
	}
}

func TestPatchAuthFileFieldsDoesNotMutateCallerFields(t *testing.T) {
	t.Parallel()

	client := &Client{
		baseURL: "http://example.test",
		http: &http.Client{
			Transport: roundTripFunc(func(*http.Request) (*http.Response, error) {
				return &http.Response{
					StatusCode: http.StatusOK,
					Body:       io.NopCloser(strings.NewReader(`{"status":"ok"}`)),
				}, nil
			}),
		},
	}
	fields := map[string]any{"priority": 3}

	if err := client.PatchAuthFileFields("auth.json", fields); err != nil {
		t.Fatalf("PatchAuthFileFields() error = %v", err)
	}
	if _, ok := fields["name"]; ok {
		t.Fatalf("caller fields mutated with name: %#v", fields)
	}
}

func TestExtractListConvertsDecodedArray(t *testing.T) {
	t.Parallel()

	got, err := extractList(map[string]any{
		"files": []any{
			map[string]any{"name": "a.json"},
			map[string]any{"name": "b.json"},
		},
	}, "files")
	if err != nil {
		t.Fatalf("extractList() error = %v", err)
	}
	if len(got) != 2 {
		t.Fatalf("len = %d, want 2", len(got))
	}
	if got[0]["name"] != "a.json" || got[1]["name"] != "b.json" {
		t.Fatalf("unexpected list: %#v", got)
	}
}

func TestExtractListRejectsNonObjectItems(t *testing.T) {
	t.Parallel()

	_, err := extractList(map[string]any{
		"files": []any{"not-an-object"},
	}, "files")
	if err == nil {
		t.Fatal("expected error")
	}
}

func TestExtractListCopiesTopLevelMaps(t *testing.T) {
	t.Parallel()

	source := map[string]any{"name": "a.json"}
	got, err := extractList(map[string]any{
		"files": []any{source},
	}, "files")
	if err != nil {
		t.Fatalf("extractList() error = %v", err)
	}
	got[0]["name"] = "changed.json"

	if source["name"] != "a.json" {
		t.Fatalf("source map mutated: %#v", source)
	}
}

func TestExtractStringListConvertsDecodedArray(t *testing.T) {
	t.Parallel()

	got, err := extractStringList(map[string]any{
		"api-keys": []any{"key-a", "key-b"},
	}, "api-keys")
	if err != nil {
		t.Fatalf("extractStringList() error = %v", err)
	}
	if len(got) != 2 || got[0] != "key-a" || got[1] != "key-b" {
		t.Fatalf("unexpected list: %#v", got)
	}
}

func TestExtractStringListRejectsNonStringItems(t *testing.T) {
	t.Parallel()

	_, err := extractStringList(map[string]any{
		"api-keys": []any{3},
	}, "api-keys")
	if err == nil {
		t.Fatal("expected error")
	}
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
