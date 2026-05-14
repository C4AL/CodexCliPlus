import { apiClient } from './client';

export interface LatestVersionInfo {
  currentVersion: string | null;
  latestVersion: string | null;
  managementCurrentVersion: string | null;
  managementLatestVersion: string | null;
  repository: string | null;
  releasePage: string | null;
  managementSource: string | null;
  assetName: string | null;
  installNote: string | null;
  installSupported: boolean;
  updateAvailable: boolean | null;
  serverUpdateAvailable: boolean | null;
  managementUpdateAvailable: boolean | null;
  raw: Record<string, unknown>;
}

export interface InstallLatestResult {
  status: string;
  currentVersion: string | null;
  latestVersion: string | null;
  repository: string | null;
  releasePage: string | null;
  restartRequired: boolean;
  message: string | null;
  raw: Record<string, unknown>;
}

const toRecord = (value: unknown): Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};

const readString = (value: unknown): string | null => {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
};

const readBoolean = (value: unknown): boolean | null => {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return value !== 0;
  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase();
    if (['true', '1', 'yes', 'y', 'on'].includes(normalized)) return true;
    if (['false', '0', 'no', 'n', 'off'].includes(normalized)) return false;
  }
  return null;
};

const parseVersionSegments = (version?: string | null) => {
  if (!version) return null;
  const cleaned = version.trim().replace(/^v/i, '');
  if (!cleaned) return null;
  const parts = cleaned
    .split(/[^0-9]+/)
    .filter(Boolean)
    .map((segment) => Number.parseInt(segment, 10))
    .filter(Number.isFinite);
  return parts.length ? parts : null;
};

const compareVersions = (latest?: string | null, current?: string | null) => {
  const latestParts = parseVersionSegments(latest);
  const currentParts = parseVersionSegments(current);
  if (!latestParts || !currentParts) return null;
  const length = Math.max(latestParts.length, currentParts.length);
  for (let index = 0; index < length; index += 1) {
    const latestPart = latestParts[index] || 0;
    const currentPart = currentParts[index] || 0;
    if (latestPart > currentPart) return 1;
    if (latestPart < currentPart) return -1;
  }
  return 0;
};

export function normalizeLatestVersionPayload(
  payload: unknown,
  currentServerVersion?: string | null,
  currentManagementVersion?: string | null
): LatestVersionInfo {
  const raw = toRecord(payload);

  const latestVersion =
    readString(raw['latest-version']) ??
    readString(raw.latest_version) ??
    readString(raw.latestVersion) ??
    readString(raw.latest) ??
    null;
  const currentVersion =
    readString(raw['current-version']) ??
    readString(raw.current_version) ??
    readString(raw.currentVersion) ??
    readString(raw.current) ??
    currentServerVersion ??
    null;
  const managementCurrentVersion =
    readString(raw['management-current-version']) ??
    readString(raw.management_current_version) ??
    readString(raw.managementCurrentVersion) ??
    readString(raw['management-current']) ??
    readString(raw.management_current) ??
    readString(raw.managementCurrent) ??
    currentManagementVersion ??
    null;
  const managementLatestVersion =
    readString(raw['management-latest-version']) ??
    readString(raw.management_latest_version) ??
    readString(raw.managementLatestVersion) ??
    readString(raw['management-latest']) ??
    readString(raw.management_latest) ??
    readString(raw.managementLatest) ??
    latestVersion;

  const repository =
    readString(raw.repository) ??
    readString(raw['repository-url']) ??
    readString(raw.repository_url) ??
    readString(raw.repositoryUrl) ??
    null;
  const releasePage =
    readString(raw['release-page']) ??
    readString(raw.release_page) ??
    readString(raw.releasePage) ??
    null;
  const managementSource =
    readString(raw['management-source']) ??
    readString(raw.management_source) ??
    readString(raw.managementSource) ??
    null;
  const assetName =
    readString(raw['asset-name']) ??
    readString(raw.asset_name) ??
    readString(raw.assetName) ??
    null;
  const installNote =
    readString(raw['install-note']) ??
    readString(raw.install_note) ??
    readString(raw.installNote) ??
    null;

  const installSupported =
    readBoolean(raw['install-supported']) ??
    readBoolean(raw.install_supported) ??
    readBoolean(raw.installSupported) ??
    false;

  let serverUpdateAvailable =
    readBoolean(raw['server-update-available']) ??
    readBoolean(raw.server_update_available) ??
    readBoolean(raw.serverUpdateAvailable);
  if (serverUpdateAvailable === null) {
    const comparison = compareVersions(latestVersion, currentVersion);
    serverUpdateAvailable = comparison === null ? null : comparison > 0;
  }

  let managementUpdateAvailable =
    readBoolean(raw['management-update-available']) ??
    readBoolean(raw.management_update_available) ??
    readBoolean(raw.managementUpdateAvailable);
  if (managementUpdateAvailable === null) {
    const comparison = compareVersions(managementLatestVersion, managementCurrentVersion);
    managementUpdateAvailable = comparison === null ? null : comparison > 0;
  }

  let updateAvailable =
    readBoolean(raw['update-available']) ??
    readBoolean(raw.update_available) ??
    readBoolean(raw.updateAvailable);
  if (updateAvailable === null) {
    if (serverUpdateAvailable !== null || managementUpdateAvailable !== null) {
      updateAvailable = Boolean(serverUpdateAvailable) || Boolean(managementUpdateAvailable);
    } else {
      const comparison = compareVersions(latestVersion, currentVersion);
      updateAvailable = comparison === null ? null : comparison > 0;
    }
  }

  return {
    currentVersion,
    latestVersion,
    managementCurrentVersion,
    managementLatestVersion,
    repository,
    releasePage,
    managementSource,
    assetName,
    installNote,
    installSupported,
    updateAvailable,
    serverUpdateAvailable,
    managementUpdateAvailable,
    raw,
  };
}

export function normalizeInstallLatestResponse(payload: unknown): InstallLatestResult {
  const raw = toRecord(payload);
  const status = readString(raw.status) ?? 'unknown';
  const currentVersion =
    readString(raw['current-version']) ??
    readString(raw.current_version) ??
    readString(raw.currentVersion) ??
    null;
  const latestVersion =
    readString(raw['latest-version']) ??
    readString(raw.latest_version) ??
    readString(raw.latestVersion) ??
    null;
  const repository = readString(raw.repository) ?? null;
  const releasePage =
    readString(raw['release-page']) ??
    readString(raw.release_page) ??
    readString(raw.releasePage) ??
    null;
  const restartRequired =
    readBoolean(raw['restart-required']) ??
    readBoolean(raw.restart_required) ??
    readBoolean(raw.restartRequired) ??
    false;
  const message = readString(raw.message) ?? readString(raw.error) ?? null;

  return {
    status,
    currentVersion,
    latestVersion,
    repository,
    releasePage,
    restartRequired,
    message,
    raw,
  };
}

export const versionApi = {
  checkLatest: async (
    currentServerVersion?: string | null,
    currentManagementVersion?: string | null
  ) =>
    normalizeLatestVersionPayload(
      await apiClient.get<Record<string, unknown>>('/latest-version'),
      currentServerVersion,
      currentManagementVersion
    ),

  installLatest: async () =>
    normalizeInstallLatestResponse(
      await apiClient.post<Record<string, unknown>>('/install-update')
    ),
};
