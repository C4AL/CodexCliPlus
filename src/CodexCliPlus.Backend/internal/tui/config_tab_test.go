package tui

import "testing"

func TestConfigTabParseConfigTreatsForceModelPrefixAsBool(t *testing.T) {
	t.Parallel()

	fields := (configTabModel{}).parseConfig(map[string]any{
		"force-model-prefix": true,
	})

	var found *configField
	for i := range fields {
		if fields[i].apiPath == "force-model-prefix" {
			found = &fields[i]
			break
		}
	}
	if found == nil {
		t.Fatal("force-model-prefix field not found")
	}
	if found.kind != "bool" {
		t.Fatalf("force-model-prefix kind = %q, want %q", found.kind, "bool")
	}
	if found.value != "true" {
		t.Fatalf("force-model-prefix value = %q, want %q", found.value, "true")
	}
}
