import { act, renderHook } from '@testing-library/react';
import { parse as parseYaml } from 'yaml';
import { describe, expect, it } from 'vitest';
import { useVisualConfig } from '@/hooks/useVisualConfig';

function parseRecord(yaml: string): Record<string, unknown> {
  return (parseYaml(yaml) ?? {}) as Record<string, unknown>;
}

describe('useVisualConfig disable-image-generation', () => {
  it('loads the false, true, and chat modes from YAML', () => {
    const { result } = renderHook(() => useVisualConfig());

    act(() => {
      result.current.loadVisualValuesFromYaml('disable-image-generation: false\n');
    });
    expect(result.current.visualValues.disableImageGeneration).toBe('false');

    act(() => {
      result.current.loadVisualValuesFromYaml('disable-image-generation: true\n');
    });
    expect(result.current.visualValues.disableImageGeneration).toBe('true');

    act(() => {
      result.current.loadVisualValuesFromYaml('disable-image-generation: chat\n');
    });
    expect(result.current.visualValues.disableImageGeneration).toBe('chat');
  });

  it('writes the selected mode without losing the YAML field', () => {
    const { result } = renderHook(() => useVisualConfig());

    act(() => {
      result.current.loadVisualValuesFromYaml('disable-image-generation: false\n');
    });

    act(() => {
      result.current.setVisualValues({ disableImageGeneration: 'true' });
    });
    expect(
      parseRecord(result.current.applyVisualChangesToYaml('disable-image-generation: false\n'))[
        'disable-image-generation'
      ]
    ).toBe(true);

    act(() => {
      result.current.setVisualValues({ disableImageGeneration: 'chat' });
    });
    expect(
      parseRecord(result.current.applyVisualChangesToYaml('disable-image-generation: true\n'))[
        'disable-image-generation'
      ]
    ).toBe('chat');

    act(() => {
      result.current.setVisualValues({ disableImageGeneration: 'false' });
    });
    expect(
      parseRecord(result.current.applyVisualChangesToYaml('disable-image-generation: chat\n'))[
        'disable-image-generation'
      ]
    ).toBe(false);
  });

  it('does not force the default field into older YAML files', () => {
    const { result } = renderHook(() => useVisualConfig());
    const yaml = 'port: 8317\n';

    act(() => {
      result.current.loadVisualValuesFromYaml(yaml);
    });

    const unchanged = result.current.applyVisualChangesToYaml(yaml);
    expect(unchanged).not.toContain('disable-image-generation');

    act(() => {
      result.current.setVisualValues({ disableImageGeneration: 'chat' });
    });
    expect(
      parseRecord(result.current.applyVisualChangesToYaml(yaml))['disable-image-generation']
    ).toBe('chat');
  });
});
