import { describe, expect, it } from 'vitest';
import type { AuthFileItem } from '@/types';
import { resolveCodexChatgptAccountId } from '@/utils/quota';

describe('resolveCodexChatgptAccountId', () => {
  it('reads top-level Codex account fields before id_token', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      chatgpt_account_id: 'acct-top-level',
      id_token: makeCodexIdToken('acct-token')
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-top-level');
  });

  it('reads metadata Codex account fields', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      metadata: {
        chatgptAccountId: 'acct-metadata'
      }
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-metadata');
  });

  it('reads attributes Codex account fallback fields', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      attributes: {
        account_id: 'acct-attributes'
      }
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-attributes');
  });

  it('reads camel-case accountId fallback fields', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      accountId: 'acct-camel'
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-camel');
  });

  it('falls back to object id_token claims', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      id_token: {
        chatgpt_account_id: 'acct-object-token'
      }
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-object-token');
  });

  it('falls back to JWT id_token claims', () => {
    const file = {
      name: 'codex.json',
      type: 'codex',
      metadata: {
        id_token: makeCodexIdToken('acct-jwt-token')
      }
    } satisfies AuthFileItem;

    expect(resolveCodexChatgptAccountId(file)).toBe('acct-jwt-token');
  });
});

function makeCodexIdToken(accountId: string) {
  const header = encodeBase64Url(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const payload = encodeBase64Url(JSON.stringify({ chatgpt_account_id: accountId }));
  return `${header}.${payload}.signature`;
}

function encodeBase64Url(value: string) {
  return btoa(value)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');
}
