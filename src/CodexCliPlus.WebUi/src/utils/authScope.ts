export interface AuthScopeSource {
  apiBase?: string;
  managementKey?: string;
  desktopSessionId?: string;
}

export function createAuthScopeKey(source: AuthScopeSource): string {
  const apiBase = source.apiBase ?? '';
  const credentialScope = source.desktopSessionId || source.managementKey || '';
  return `${apiBase}::${credentialScope}`;
}
