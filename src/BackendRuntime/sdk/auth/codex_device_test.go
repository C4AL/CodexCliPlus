package auth

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestRequestCodexDeviceUserCodeReturnsCloseFailure(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("device code close failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{"device_auth_id":"device","user_code":"code","interval":1}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	resp, err := requestCodexDeviceUserCode(context.Background(), client)
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "failed to close codex device code response") {
		t.Fatalf("requestCodexDeviceUserCode error = %v, want close failure", err)
	}
	if resp != nil {
		t.Fatalf("response = %#v, want nil", resp)
	}
}

func TestPollCodexDeviceTokenReturnsCloseFailureOnSuccess(t *testing.T) {
	t.Parallel()

	closeErr := errors.New("device poll close failed")
	client := &http.Client{Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Method != http.MethodPost {
			t.Fatalf("request method = %s, want POST", req.Method)
		}
		return &http.Response{
			StatusCode: http.StatusOK,
			Body: closeErrorReadCloser{
				Reader: strings.NewReader(`{"authorization_code":"auth","code_verifier":"verifier","code_challenge":"challenge"}`),
				err:    closeErr,
			},
			Header:  make(http.Header),
			Request: req,
		}, nil
	})}

	resp, err := pollCodexDeviceToken(context.Background(), client, "device", "code", 0)
	if !errors.Is(err, closeErr) || !strings.Contains(err.Error(), "failed to close codex device poll response") {
		t.Fatalf("pollCodexDeviceToken error = %v, want close failure", err)
	}
	if resp != nil {
		t.Fatalf("response = %#v, want nil", resp)
	}
}

type closeErrorReadCloser struct {
	io.Reader
	err error
}

func (r closeErrorReadCloser) Close() error {
	return r.err
}
