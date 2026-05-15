package management

import (
	"strings"
	"testing"
)

func TestParseOAuthCallbackPayloadParsesFields(t *testing.T) {
	t.Parallel()

	got, err := parseOAuthCallbackPayload([]byte(`{"code":"auth-code","state":"state-1"}`))
	if err != nil {
		t.Fatalf("parseOAuthCallbackPayload returned error: %v", err)
	}
	if got["code"] != "auth-code" {
		t.Fatalf("code = %q, want auth-code", got["code"])
	}
	if got["state"] != "state-1" {
		t.Fatalf("state = %q, want state-1", got["state"])
	}
}

func TestParseOAuthCallbackPayloadRejectsMalformedJSON(t *testing.T) {
	t.Parallel()

	got, err := parseOAuthCallbackPayload([]byte(`{"code":`))
	if err == nil || !strings.Contains(err.Error(), "parse OAuth callback payload") {
		t.Fatalf("parseOAuthCallbackPayload error = %v, want parse failure", err)
	}
	if got != nil {
		t.Fatalf("payload = %#v, want nil", got)
	}
}

func TestParseOAuthCallbackPayloadTreatsNullAsEmptyMap(t *testing.T) {
	t.Parallel()

	got, err := parseOAuthCallbackPayload([]byte(`null`))
	if err != nil {
		t.Fatalf("parseOAuthCallbackPayload returned error: %v", err)
	}
	if got == nil {
		t.Fatal("payload is nil, want empty map")
	}
	if len(got) != 0 {
		t.Fatalf("payload = %#v, want empty map", got)
	}
}
