import { describe, expect, it, vi } from 'vitest';
import {
  getManagementAccessBlockedMessage,
  runAccountRefreshSteps,
} from '@/features/accountCenter/refresh';

describe('account center refresh guard', () => {
  it('runs refresh steps in order', async () => {
    const calls: string[] = [];

    const result = await runAccountRefreshSteps([
      { id: 'auth-files', run: vi.fn(async () => calls.push('auth-files')) },
      { id: 'usage', run: vi.fn(async () => calls.push('usage')) },
      { id: 'oauth', run: vi.fn(async () => calls.push('oauth')) },
    ]);

    expect(result.stopped).toBe(false);
    expect(calls).toEqual(['auth-files', 'usage', 'oauth']);
  });

  it('stops later refresh steps after management auth is blocked', async () => {
    const blockedError = Object.assign(new Error('too many failures, banned for 30 minutes'), {
      status: 403,
    });
    const calls: string[] = [];
    const onBlocked = vi.fn();

    const result = await runAccountRefreshSteps(
      [
        {
          id: 'auth-files',
          run: vi.fn(async () => {
            calls.push('auth-files');
            throw blockedError;
          }),
        },
        { id: 'usage', run: vi.fn(async () => calls.push('usage')) },
      ],
      onBlocked
    );

    expect(result.stopped).toBe(true);
    expect(calls).toEqual(['auth-files']);
    expect(onBlocked).toHaveBeenCalledWith(
      '管理接口认证失败或已被临时封禁，请确认安全密钥未变化；如果后端已封禁，请等待约 30 分钟后重试。',
      blockedError
    );
  });

  it('detects ban text even when the status code is absent', () => {
    expect(getManagementAccessBlockedMessage(new Error('client banned for 30 minutes'))).toBe(
      '管理接口认证失败或已被临时封禁，请确认安全密钥未变化；如果后端已封禁，请等待约 30 分钟后重试。'
    );
  });
});
