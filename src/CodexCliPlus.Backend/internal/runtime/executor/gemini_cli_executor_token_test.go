package executor

import (
	"encoding/json"
	"reflect"
	"testing"
	"time"

	"golang.org/x/oauth2"
)

func TestGeminiTokenFromMapParsesKnownFields(t *testing.T) {
	t.Parallel()

	expiry := time.Date(2026, 2, 3, 4, 5, 6, 123456789, time.UTC)
	got := geminiTokenFromMap(map[string]any{
		"access_token":  "access-token",
		"token_type":    "Bearer",
		"refresh_token": "refresh-token",
		"expiry":        expiry.Format(time.RFC3339Nano),
		"expires_in":    float64(3600),
	})

	if got.AccessToken != "access-token" {
		t.Fatalf("AccessToken = %q, want access-token", got.AccessToken)
	}
	if got.TokenType != "Bearer" {
		t.Fatalf("TokenType = %q, want Bearer", got.TokenType)
	}
	if got.RefreshToken != "refresh-token" {
		t.Fatalf("RefreshToken = %q, want refresh-token", got.RefreshToken)
	}
	if !got.Expiry.Equal(expiry) {
		t.Fatalf("Expiry = %s, want %s", got.Expiry.Format(time.RFC3339Nano), expiry.Format(time.RFC3339Nano))
	}
	if got.ExpiresIn != 3600 {
		t.Fatalf("ExpiresIn = %d, want 3600", got.ExpiresIn)
	}
}

func TestGeminiTokenFromMapKeepsFieldsWhenExpiryIsInvalid(t *testing.T) {
	t.Parallel()

	got := geminiTokenFromMap(map[string]any{
		"access_token":  "access-token",
		"refresh_token": "refresh-token",
		"expiry":        "not-a-time",
	})

	if got.AccessToken != "access-token" {
		t.Fatalf("AccessToken = %q, want access-token", got.AccessToken)
	}
	if got.RefreshToken != "refresh-token" {
		t.Fatalf("RefreshToken = %q, want refresh-token", got.RefreshToken)
	}
	if !got.Expiry.IsZero() {
		t.Fatalf("Expiry = %s, want zero value", got.Expiry.Format(time.RFC3339Nano))
	}
}

func TestBuildGeminiTokenMapMatchesTokenJSONFields(t *testing.T) {
	t.Parallel()

	cases := []struct {
		name string
		tok  *oauth2.Token
	}{
		{
			name: "full token",
			tok: &oauth2.Token{
				AccessToken:  "new-token",
				TokenType:    "Bearer",
				RefreshToken: "refresh-token",
				Expiry:       time.Date(2026, 1, 2, 3, 4, 5, 123456789, time.UTC),
				ExpiresIn:    3600,
			},
		},
		{
			name: "zero expiry token",
			tok: &oauth2.Token{
				AccessToken: "new-token",
			},
		},
	}

	for _, tc := range cases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			base := map[string]any{
				"access_token":  "old-token",
				"custom":        "kept",
				"expires_in":    float64(12),
				"refresh_token": "old-refresh",
				"token_type":    "old-type",
			}
			got := buildGeminiTokenMap(base, tc.tok)

			want := cloneMap(base)
			raw, errMarshal := json.Marshal(tc.tok)
			if errMarshal != nil {
				t.Fatalf("json.Marshal token returned error: %v", errMarshal)
			}
			var tokenMap map[string]any
			if errUnmarshal := json.Unmarshal(raw, &tokenMap); errUnmarshal != nil {
				t.Fatalf("json.Unmarshal token returned error: %v", errUnmarshal)
			}
			for k, v := range tokenMap {
				want[k] = v
			}

			if !reflect.DeepEqual(got, want) {
				t.Fatalf("token map = %#v, want %#v", got, want)
			}
			if base["access_token"] != "old-token" {
				t.Fatalf("base map was mutated: %#v", base)
			}
		})
	}
}
