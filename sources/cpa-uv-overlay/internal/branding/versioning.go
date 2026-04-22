package branding

import (
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

const DefaultBaselineVersion = "6.9.31"
const DefaultManagementBaselineVersion = "1.7.41"

type mismatchStrategy string

const (
	mismatchStrategyBaselineAsUV mismatchStrategy = "baseline-as-uv"
	mismatchStrategyPreserveUV   mismatchStrategy = "preserve-uv"
)

var (
	baselineUVVersionPattern = regexp.MustCompile(`(?i)^v?(\d+(?:\.\d+){2})(?:[-_.]?uv[-_.]?(\d+(?:[-_.]\d+)*))?$`)
	uvOnlyVersionPattern     = regexp.MustCompile(`(?i)^v?(\d+(?:\.\d+){0,2})$`)
	displayVersionPattern    = regexp.MustCompile(`(?i)^(\d+(?:\.\d+){2})-uv\s*\((\d+(?:\.\d+)*)\)$`)
	gitDescribePattern       = regexp.MustCompile(`(?i)^(.+)-\d+-g[0-9a-f]+(?:-dirty)?$`)
)

type VersionInfo struct {
	Raw             string
	Display         string
	BaselineVersion string
	UVVersion       string
	comparableParts []int
}

func NormalizeVersion(raw string) VersionInfo {
	return normalizeVersion(raw, DefaultBaselineVersion, mismatchStrategyBaselineAsUV)
}

func NormalizeManagementVersion(raw string) VersionInfo {
	return normalizeVersion(raw, DefaultManagementBaselineVersion, mismatchStrategyPreserveUV)
}

func normalizeVersion(raw string, baselineVersion string, strategy mismatchStrategy) VersionInfo {
	raw = normalizeVersionSource(raw)
	if raw == "" {
		return VersionInfo{Display: "unknown"}
	}

	if matches := displayVersionPattern.FindStringSubmatch(raw); len(matches) == 3 {
		baseline := matches[1]
		uvVersion := normalizeUVVersion(matches[2])
		return VersionInfo{
			Raw:             raw,
			Display:         buildDisplayVersion(baseline, uvVersion),
			BaselineVersion: baseline,
			UVVersion:       uvVersion,
			comparableParts: buildComparableParts(splitVersionParts(baseline), splitVersionParts(uvVersion)),
		}
	}

	if matches := baselineUVVersionPattern.FindStringSubmatch(raw); len(matches) == 3 {
		baseline := matches[1]
		uvVersion := normalizeUVVersion(matches[2])
		if baseline != baselineVersion {
			if strategy == mismatchStrategyPreserveUV && uvVersion != "" {
				baseline = baselineVersion
			} else {
				uvVersion = normalizeUVVersion(baseline)
				baseline = baselineVersion
			}
		} else if uvVersion == "" {
			uvVersion = "1.0.0"
		}
		return VersionInfo{
			Raw:             raw,
			Display:         buildDisplayVersion(baseline, uvVersion),
			BaselineVersion: baseline,
			UVVersion:       uvVersion,
			comparableParts: buildComparableParts(splitVersionParts(baseline), splitVersionParts(uvVersion)),
		}
	}

	if matches := uvOnlyVersionPattern.FindStringSubmatch(raw); len(matches) == 2 {
		baseline := baselineVersion
		uvVersion := normalizeUVVersion(matches[1])
		return VersionInfo{
			Raw:             raw,
			Display:         buildDisplayVersion(baseline, uvVersion),
			BaselineVersion: baseline,
			UVVersion:       uvVersion,
			comparableParts: buildComparableParts(splitVersionParts(baseline), splitVersionParts(uvVersion)),
		}
	}

	return VersionInfo{
		Raw:             raw,
		Display:         raw,
		BaselineVersion: "",
		UVVersion:       "",
		comparableParts: splitVersionParts(raw),
	}
}

func normalizeVersionSource(raw string) string {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return ""
	}
	if matches := gitDescribePattern.FindStringSubmatch(raw); len(matches) == 2 {
		raw = matches[1]
	}
	raw = strings.TrimSuffix(raw, "-dirty")
	return strings.TrimSpace(raw)
}

func CompareVersions(leftRaw, rightRaw string) int {
	left := NormalizeVersion(leftRaw)
	right := NormalizeVersion(rightRaw)
	return compareVersionParts(left.comparableParts, right.comparableParts)
}

func CompareManagementVersions(leftRaw, rightRaw string) int {
	left := NormalizeManagementVersion(leftRaw)
	right := NormalizeManagementVersion(rightRaw)
	return compareVersionParts(left.comparableParts, right.comparableParts)
}

func buildDisplayVersion(baseline, uvVersion string) string {
	if baseline == "" || uvVersion == "" {
		return strings.TrimSpace(baseline + uvVersion)
	}
	return fmt.Sprintf("%s-UV (%s)", baseline, uvVersion)
}

func normalizeUVVersion(raw string) string {
	parts := splitVersionParts(raw)
	if len(parts) == 0 {
		return ""
	}
	for len(parts) < 3 {
		parts = append(parts, 0)
	}
	text := make([]string, 0, len(parts))
	for _, part := range parts {
		text = append(text, strconv.Itoa(part))
	}
	return strings.Join(text, ".")
}

func splitVersionParts(raw string) []int {
	segments := strings.FieldsFunc(strings.TrimSpace(raw), func(r rune) bool {
		return r < '0' || r > '9'
	})
	parts := make([]int, 0, len(segments))
	for _, segment := range segments {
		if segment == "" {
			continue
		}
		value, err := strconv.Atoi(segment)
		if err != nil {
			continue
		}
		parts = append(parts, value)
	}
	return parts
}

func buildComparableParts(left, right []int) []int {
	parts := make([]int, 0, len(left)+len(right))
	parts = append(parts, left...)
	parts = append(parts, right...)
	return parts
}

func compareVersionParts(left, right []int) int {
	length := len(left)
	if len(right) > length {
		length = len(right)
	}
	for i := 0; i < length; i++ {
		lv := 0
		if i < len(left) {
			lv = left[i]
		}
		rv := 0
		if i < len(right) {
			rv = right[i]
		}
		if lv > rv {
			return 1
		}
		if lv < rv {
			return -1
		}
	}
	return 0
}
