import { describe, expect, it } from 'vitest';
import { buildConfigDiff } from '@/utils/configDiff';

describe('buildConfigDiff', () => {
  it('returns no hunks for unchanged config', () => {
    const diff = buildConfigDiff('server:\n  port: 8080\n', 'server:\n  port: 8080\n');

    expect(diff.file).toBeNull();
    expect(diff.hunks).toHaveLength(0);
    expect(diff.additions).toBe(0);
    expect(diff.deletions).toBe(0);
  });

  it('counts pure additions', () => {
    const diff = buildConfigDiff('', 'server:\n  port: 8080\n');

    expect(diff.file?.type).toBe('modify');
    expect(diff.hunks).toHaveLength(1);
    expect(diff.additions).toBe(2);
    expect(diff.deletions).toBe(0);
  });

  it('counts pure deletions', () => {
    const diff = buildConfigDiff('server:\n  port: 8080\n', '');

    expect(diff.file?.type).toBe('modify');
    expect(diff.hunks).toHaveLength(1);
    expect(diff.additions).toBe(0);
    expect(diff.deletions).toBe(2);
  });
});
