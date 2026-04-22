// Package antigravity provides OAuth2 authentication functionality for the Antigravity provider.
package antigravity

import (
	"fmt"
	"os"
	"strings"
)

const (
	CallbackPort       = 51121
	ClientIDEnvVar     = "CPA_UV_ANTIGRAVITY_OAUTH_CLIENT_ID"
	ClientSecretEnvVar = "CPA_UV_ANTIGRAVITY_OAUTH_CLIENT_SECRET"
)

func ClientID() string {
	return strings.TrimSpace(os.Getenv(ClientIDEnvVar))
}

func ClientSecret() string {
	return strings.TrimSpace(os.Getenv(ClientSecretEnvVar))
}

func ValidateOAuthCredentials() error {
	if ClientID() != "" && ClientSecret() != "" {
		return nil
	}
	return fmt.Errorf(
		"antigravity oauth client credentials are not configured: set %s and %s",
		ClientIDEnvVar,
		ClientSecretEnvVar,
	)
}

// Scopes defines the OAuth scopes required for Antigravity authentication
var Scopes = []string{
	"https://www.googleapis.com/auth/cloud-platform",
	"https://www.googleapis.com/auth/userinfo.email",
	"https://www.googleapis.com/auth/userinfo.profile",
	"https://www.googleapis.com/auth/cclog",
	"https://www.googleapis.com/auth/experimentsandconfigs",
}

// OAuth2 endpoints for Google authentication
const (
	TokenEndpoint    = "https://oauth2.googleapis.com/token"
	AuthEndpoint     = "https://accounts.google.com/o/oauth2/v2/auth"
	UserInfoEndpoint = "https://www.googleapis.com/oauth2/v1/userinfo?alt=json"
)

// Antigravity API configuration
const (
	APIEndpoint    = "https://cloudcode-pa.googleapis.com"
	APIVersion     = "v1internal"
	APIUserAgent   = "google-api-nodejs-client/9.15.1"
	APIClient      = "google-cloud-sdk vscode_cloudshelleditor/0.1"
	ClientMetadata = `{"ideType":"IDE_UNSPECIFIED","platform":"PLATFORM_UNSPECIFIED","pluginType":"GEMINI"}`
)
