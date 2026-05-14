import { describe, expect, it } from 'vitest';
import { buildCandidateUsageSourceIds, computeKeyStatsFromApiKeyUsage } from '@/utils/usage';

describe('computeKeyStatsFromApiKeyUsage', () => {
  it('maps v6.10 api-key usage into source stats and status data', () => {
    const apiKey = 'fixture-token-12345';
    const stats = computeKeyStatsFromApiKeyUsage({
      codex: {
        [`https://chatgpt.com/backend-api/codex|${apiKey}`]: {
          success: 4,
          failed: 1,
          recent_requests: [{ time: '10:00-10:10', success: 2, failed: 1 }],
        },
      },
    });

    const candidate = buildCandidateUsageSourceIds({ apiKey })[0];
    expect(stats.bySource[candidate]).toEqual({ success: 4, failure: 1 });
    expect(stats.statusBySource?.[candidate]?.totalSuccess).toBe(2);
    expect(stats.statusBySource?.[candidate]?.totalFailure).toBe(1);
  });
});
