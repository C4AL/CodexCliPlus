package registry

import (
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestReadModelsResponseBodyReturnsCloseFailure(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("models body close failed")
	resp := &http.Response{
		Body: closeErrorReadCloser{
			Reader: strings.NewReader(`{"claude":[{"id":"claude-test"}]}`),
			err:    closeErr,
		},
	}

	data, err := readModelsResponseBody(resp, "https://models.example/models.json")
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "close models response body") {
		t.Fatalf("readModelsResponseBody error = %v, want close failure", err)
	}
	if data != nil {
		t.Fatalf("data = %q, want nil", data)
	}
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
