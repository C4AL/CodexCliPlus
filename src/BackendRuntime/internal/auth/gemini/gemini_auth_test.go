package gemini

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"reflect"
	"strings"
	"testing"
	"time"

	"golang.org/x/oauth2"
)

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) {
	return f(req)
}

type failingReadCloser struct {
	err error
}

func (r failingReadCloser) Read([]byte) (int, error) {
	return 0, r.err
}

func (r failingReadCloser) Close() error {
	return nil
}

func TestCreateTokenStorageReturnsUserInfoReadError(t *testing.T) {
	readErr := errors.New("broken user info body")
	httpClient := &http.Client{
		Transport: roundTripFunc(func(req *http.Request) (*http.Response, error) {
			return &http.Response{
				StatusCode: http.StatusOK,
				Header:     make(http.Header),
				Body:       failingReadCloser{err: readErr},
				Request:    req,
			}, nil
		}),
	}
	ctx := context.WithValue(context.Background(), oauth2.HTTPClient, httpClient)
	token := &oauth2.Token{
		AccessToken: "access-token",
		Expiry:      time.Now().Add(time.Hour),
	}

	_, err := NewGeminiAuth().createTokenStorage(ctx, &oauth2.Config{}, token, "project-id")
	if !errors.Is(err, readErr) {
		t.Fatalf("error = %v, want wrapped read error", err)
	}
	if !strings.Contains(err.Error(), "failed to read user info response") {
		t.Fatalf("error = %q, want user info read context", err.Error())
	}
}

func TestGeminiOAuthTokenMapMatchesTokenJSONFields(t *testing.T) {
	cases := []struct {
		name  string
		token *oauth2.Token
	}{
		{
			name: "full token",
			token: &oauth2.Token{
				AccessToken:  "access-token",
				TokenType:    "Bearer",
				RefreshToken: "refresh-token",
				Expiry:       time.Date(2026, 1, 2, 3, 4, 5, 123456789, time.UTC),
				ExpiresIn:    3600,
			},
		},
		{
			name: "zero expiry token",
			token: &oauth2.Token{
				AccessToken: "access-token",
			},
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got := geminiOAuthTokenMap(tc.token)

			raw, errMarshal := json.Marshal(tc.token)
			if errMarshal != nil {
				t.Fatalf("json.Marshal token returned error: %v", errMarshal)
			}
			var want map[string]any
			if errUnmarshal := json.Unmarshal(raw, &want); errUnmarshal != nil {
				t.Fatalf("json.Unmarshal token returned error: %v", errUnmarshal)
			}

			if !reflect.DeepEqual(got, want) {
				t.Fatalf("token map = %#v, want %#v", got, want)
			}
		})
	}
}
