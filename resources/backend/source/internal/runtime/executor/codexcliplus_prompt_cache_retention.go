package executor

import (
	"strings"

	"github.com/tidwall/gjson"
	"github.com/tidwall/sjson"
)

func codexCliPlusApplyPromptCacheRetention(payload []byte, fallbackModel string, protocol string) ([]byte, bool) {
	normalizedProtocol := strings.ToLower(strings.TrimSpace(protocol))
	if normalizedProtocol != "openai" && normalizedProtocol != "openai-response" {
		return payload, false
	}
	if len(payload) == 0 {
		return payload, false
	}

	existing := gjson.GetBytes(payload, "prompt_cache_retention")
	if existing.Exists() {
		mode := strings.ToLower(strings.TrimSpace(existing.String()))
		if mode != "auto" {
			return payload, false
		}
		model := codexCliPlusPromptCacheModel(payload, fallbackModel)
		if !codexCliPlusSupportsExtendedPromptCache(model) {
			updated, err := sjson.DeleteBytes(payload, "prompt_cache_retention")
			if err != nil {
				return payload, false
			}
			return updated, false
		}
		updated, err := sjson.SetBytes(payload, "prompt_cache_retention", "24h")
		if err != nil {
			return payload, false
		}
		return updated, true
	}

	model := codexCliPlusPromptCacheModel(payload, fallbackModel)
	if !codexCliPlusSupportsExtendedPromptCache(model) {
		return payload, false
	}
	updated, err := sjson.SetBytes(payload, "prompt_cache_retention", "24h")
	if err != nil {
		return payload, false
	}
	return updated, true
}

func codexCliPlusPromptCacheModel(payload []byte, fallbackModel string) string {
	if model := strings.TrimSpace(gjson.GetBytes(payload, "model").String()); model != "" {
		return model
	}
	return strings.TrimSpace(fallbackModel)
}

func codexCliPlusSupportsExtendedPromptCache(model string) bool {
	normalized := strings.ToLower(strings.TrimSpace(model))
	return strings.HasPrefix(normalized, "gpt-5.1") ||
		strings.HasPrefix(normalized, "gpt-5") ||
		strings.HasPrefix(normalized, "gpt-4.1")
}

func codexCliPlusShouldRetryWithoutPromptCacheRetention(status int, body []byte) bool {
	if status < 400 || len(body) == 0 {
		return false
	}
	text := strings.ToLower(string(body))
	if !strings.Contains(text, "prompt_cache_retention") {
		return false
	}
	for _, marker := range []string{
		"unsupported",
		"not supported",
		"unknown parameter",
		"unrecognized",
		"invalid",
		"unsupported_parameter",
		"invalid_request_error",
	} {
		if strings.Contains(text, marker) {
			return true
		}
	}
	return false
}

func codexCliPlusRemovePromptCacheRetention(payload []byte) []byte {
	updated, err := sjson.DeleteBytes(payload, "prompt_cache_retention")
	if err != nil {
		return payload
	}
	return updated
}
