import { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import type { ReactCodeMirrorRef } from '@uiw/react-codemirror';
import { parse as parseToml, stringify as stringifyToml, type TomlTable } from 'smol-toml';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Select, type SelectOption } from '@/components/ui/Select';
import {
  IconCheck,
  IconChevronDown,
  IconChevronUp,
  IconFileText,
  IconRefreshCw,
  IconSearch,
} from '@/components/ui/icons';
import { DiffModal } from '@/components/config/DiffModal';
import {
  backupCodexUserFile,
  isDesktopMode,
  readCodexUserFile,
  requestCodexRouteState,
  requestCodexUserFiles,
  saveCodexUserFile,
  switchCodexRoute,
  validateCodexUserFile,
  type CodexRouteState,
  type CodexUserFileId,
  type CodexUserFileSnapshot,
} from '@/desktop/bridge';
import { useNotificationStore, useThemeStore } from '@/stores';
import styles from './CodexConfigPage.module.scss';

const LazyConfigSourceEditor = lazy(() => import('@/components/config/ConfigSourceEditor'));

type EditorMode = 'visual' | 'source';
type TemplateKind = 'root' | 'table' | 'profile' | 'provider';
type TomlValueKind = 'string' | 'number' | 'boolean' | 'raw';
type JsonValueKind = 'string' | 'number' | 'boolean' | 'object' | 'null';

type SnapshotMap = Record<CodexUserFileId, CodexUserFileSnapshot | null>;
type DraftMap = Record<CodexUserFileId, string>;

type RootVisualState = {
  profile: string;
  model: string;
  modelProvider: string;
  reasoningEffort: string;
  sandboxMode: string;
  approvalPolicy: string;
  requiresOpenaiAuth: '' | 'true' | 'false';
  chatgptBaseUrl: string;
  cliAuthCredentialsStore: string;
  baseUrl: string;
  wireApi: string;
};

type ProfileVisualRow = {
  id: string;
  name: string;
  model: string;
  modelProvider: string;
  reasoningEffort: string;
  sandboxMode: string;
  approvalPolicy: string;
};

type ProviderVisualRow = {
  id: string;
  name: string;
  baseUrl: string;
  wireApi: string;
  envKey: string;
  requiresOpenaiAuth: '' | 'true' | 'false';
};

type ConfigVisualState = {
  root: RootVisualState;
  profiles: ProfileVisualRow[];
  providers: ProviderVisualRow[];
};

const FILE_OPTIONS: Array<{ fileId: CodexUserFileId; name: string; label: string }> = [
  { fileId: 'config', name: 'config.toml', label: 'Codex 主配置' },
  { fileId: 'auth', name: 'auth.json', label: 'Codex 认证文件' },
];

const DEFAULT_SNAPSHOTS: SnapshotMap = { config: null, auth: null };
const DEFAULT_DRAFTS: DraftMap = { config: '', auth: '' };

const EMPTY_ROOT: RootVisualState = {
  profile: '',
  model: '',
  modelProvider: '',
  reasoningEffort: '',
  sandboxMode: '',
  approvalPolicy: '',
  requiresOpenaiAuth: '',
  chatgptBaseUrl: '',
  cliAuthCredentialsStore: '',
  baseUrl: '',
  wireApi: '',
};

const EMPTY_VISUAL: ConfigVisualState = {
  root: EMPTY_ROOT,
  profiles: [],
  providers: [],
};

const modeOptions: SelectOption[] = [
  { value: 'visual', label: '可视化编辑' },
  { value: 'source', label: '源码编辑' },
];

const routeModeLabel = (mode: CodexRouteState['currentMode']) => {
  if (mode === 'cpa') return 'CPA 模式';
  if (mode === 'official') return '官方 Codex';
  return '未知模式';
};

const createRowId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

const isTomlTable = (value: unknown): value is TomlTable =>
  Boolean(value) && typeof value === 'object' && !Array.isArray(value);

const readString = (table: TomlTable, key: string) => {
  const value = table[key];
  return typeof value === 'string' ? value : '';
};

const readBooleanSelect = (table: TomlTable, key: string): '' | 'true' | 'false' => {
  const value = table[key];
  if (value === true) return 'true';
  if (value === false) return 'false';
  return '';
};

const setOptionalString = (table: TomlTable, key: string, value: string) => {
  const trimmed = value.trim();
  if (trimmed) {
    table[key] = trimmed;
  } else {
    delete table[key];
  }
};

const setOptionalBoolean = (table: TomlTable, key: string, value: '' | 'true' | 'false') => {
  if (value === 'true') {
    table[key] = true;
  } else if (value === 'false') {
    table[key] = false;
  } else {
    delete table[key];
  }
};

const parseConfigVisual = (content: string): { ok: true; value: ConfigVisualState } | { ok: false; error: string } => {
  try {
    const model = parseToml(content || '') as TomlTable;
    const profiles = isTomlTable(model.profiles)
      ? Object.entries(model.profiles)
          .filter(([, value]) => isTomlTable(value))
          .map(([name, value]) => {
            const table = value as TomlTable;
            return {
              id: createRowId(),
              name,
              model: readString(table, 'model'),
              modelProvider: readString(table, 'model_provider'),
              reasoningEffort: readString(table, 'reasoning_effort'),
              sandboxMode: readString(table, 'sandbox_mode'),
              approvalPolicy: readString(table, 'approval_policy'),
            };
          })
      : [];
    const providers = isTomlTable(model.model_providers)
      ? Object.entries(model.model_providers)
          .filter(([, value]) => isTomlTable(value))
          .map(([name, value]) => {
            const table = value as TomlTable;
            return {
              id: createRowId(),
              name,
              baseUrl: readString(table, 'base_url'),
              wireApi: readString(table, 'wire_api'),
              envKey: readString(table, 'env_key'),
              requiresOpenaiAuth: readBooleanSelect(table, 'requires_openai_auth'),
            };
          })
      : [];

    return {
      ok: true,
      value: {
        root: {
          profile: readString(model, 'profile'),
          model: readString(model, 'model'),
          modelProvider: readString(model, 'model_provider'),
          reasoningEffort: readString(model, 'reasoning_effort'),
          sandboxMode: readString(model, 'sandbox_mode'),
          approvalPolicy: readString(model, 'approval_policy'),
          requiresOpenaiAuth: readBooleanSelect(model, 'requires_openai_auth'),
          chatgptBaseUrl: readString(model, 'chatgpt_base_url'),
          cliAuthCredentialsStore: readString(model, 'cli_auth_credentials_store'),
          baseUrl: readString(model, 'base_url'),
          wireApi: readString(model, 'wire_api'),
        },
        profiles,
        providers,
      },
    };
  } catch (error) {
    return {
      ok: false,
      error: error instanceof Error ? error.message : 'TOML 解析失败',
    };
  }
};

const applyConfigVisual = (content: string, visual: ConfigVisualState) => {
  const model = parseToml(content || '') as TomlTable;
  setOptionalString(model, 'profile', visual.root.profile);
  setOptionalString(model, 'model', visual.root.model);
  setOptionalString(model, 'model_provider', visual.root.modelProvider);
  setOptionalString(model, 'reasoning_effort', visual.root.reasoningEffort);
  setOptionalString(model, 'sandbox_mode', visual.root.sandboxMode);
  setOptionalString(model, 'approval_policy', visual.root.approvalPolicy);
  setOptionalBoolean(model, 'requires_openai_auth', visual.root.requiresOpenaiAuth);
  setOptionalString(model, 'chatgpt_base_url', visual.root.chatgptBaseUrl);
  setOptionalString(model, 'cli_auth_credentials_store', visual.root.cliAuthCredentialsStore);
  setOptionalString(model, 'base_url', visual.root.baseUrl);
  setOptionalString(model, 'wire_api', visual.root.wireApi);

  const profiles = isTomlTable(model.profiles) ? { ...model.profiles } : {};
  for (const key of Object.keys(profiles)) {
    if (!visual.profiles.some((profile) => profile.name.trim() === key)) delete profiles[key];
  }
  for (const row of visual.profiles) {
    const name = row.name.trim();
    if (!name) continue;
    const table = isTomlTable(profiles[name]) ? { ...(profiles[name] as TomlTable) } : {};
    setOptionalString(table, 'model', row.model);
    setOptionalString(table, 'model_provider', row.modelProvider);
    setOptionalString(table, 'reasoning_effort', row.reasoningEffort);
    setOptionalString(table, 'sandbox_mode', row.sandboxMode);
    setOptionalString(table, 'approval_policy', row.approvalPolicy);
    profiles[name] = table;
  }
  if (Object.keys(profiles).length > 0) model.profiles = profiles;
  else delete model.profiles;

  const providers = isTomlTable(model.model_providers) ? { ...model.model_providers } : {};
  for (const key of Object.keys(providers)) {
    if (!visual.providers.some((provider) => provider.name.trim() === key)) delete providers[key];
  }
  for (const row of visual.providers) {
    const name = row.name.trim();
    if (!name) continue;
    const table = isTomlTable(providers[name]) ? { ...(providers[name] as TomlTable) } : {};
    setOptionalString(table, 'base_url', row.baseUrl);
    setOptionalString(table, 'wire_api', row.wireApi);
    setOptionalString(table, 'env_key', row.envKey);
    setOptionalBoolean(table, 'requires_openai_auth', row.requiresOpenaiAuth);
    providers[name] = table;
  }
  if (Object.keys(providers).length > 0) model.model_providers = providers;
  else delete model.model_providers;

  return stringifyToml(model).trimEnd() + '\n';
};

const formatBytes = (value: number) => {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
};

const formatTimestamp = (value?: string | null) => {
  if (!value) return '尚未创建';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '时间未知';
  return date.toLocaleString('zh-CN', { hour12: false });
};

const formatTomlKey = (key: string) =>
  /^[A-Za-z0-9_-]+$/.test(key) ? key : JSON.stringify(key);

const formatTomlTablePath = (path: string) =>
  path
    .split('.')
    .map((part) => formatTomlKey(part.trim()))
    .join('.');

const formatTomlValue = (value: string, kind: TomlValueKind) => {
  if (kind === 'number') {
    const normalized = Number(value);
    if (!Number.isFinite(normalized)) throw new Error('数值模板参数必须是有效数字');
    return String(normalized);
  }
  if (kind === 'boolean') {
    if (value !== 'true' && value !== 'false') throw new Error('布尔模板参数必须为 true 或 false');
    return value;
  }
  if (kind === 'raw') return value.trim();
  return JSON.stringify(value);
};

const parseJsonTemplateValue = (value: string, kind: JsonValueKind) => {
  if (kind === 'null') return null;
  if (kind === 'number') {
    const normalized = Number(value);
    if (!Number.isFinite(normalized)) throw new Error('数值模板参数必须是有效数字');
    return normalized;
  }
  if (kind === 'boolean') {
    if (value !== 'true' && value !== 'false') throw new Error('布尔模板参数必须为 true 或 false');
    return value === 'true';
  }
  if (kind === 'object') return JSON.parse(value || '{}');
  return value;
};

const insertSnippet = (content: string, snippet: string, editorRef: RefObject<ReactCodeMirrorRef | null>) => {
  const view = editorRef.current?.view;
  if (!view) {
    const prefix = content.trimEnd();
    return `${prefix}${prefix ? '\n\n' : ''}${snippet.trimEnd()}\n`;
  }

  const selection = view.state.selection.main;
  const doc = view.state.doc.toString();
  const before = doc.slice(0, selection.from);
  const after = doc.slice(selection.to);
  const block = `${before && !before.endsWith('\n') ? '\n' : ''}${snippet.trimEnd()}\n${after && !after.startsWith('\n') ? '\n' : ''}`;
  return `${before}${block}${after}`;
};

const resolveSelectedRouteTargetId = (state: CodexRouteState | null, selectedId: string) => {
  if (!state) return '';
  if (state.targets.some((target) => target.id === selectedId)) return selectedId;
  return (
    state.currentTargetId ||
    state.targets.find((target) => target.isCurrent)?.id ||
    state.targets[0]?.id ||
    ''
  );
};

export function CodexConfigPage() {
  const showNotification = useNotificationStore((state) => state.showNotification);
  const resolvedTheme = useThemeStore((state) => state.resolvedTheme);
  const desktopBridgeAvailable = isDesktopMode();
  const editorRef = useRef<ReactCodeMirrorRef | null>(null);

  const [activeFileId, setActiveFileId] = useState<CodexUserFileId>('config');
  const [editorMode, setEditorMode] = useState<EditorMode>('visual');
  const [snapshots, setSnapshots] = useState<SnapshotMap>(DEFAULT_SNAPSHOTS);
  const [drafts, setDrafts] = useState<DraftMap>(DEFAULT_DRAFTS);
  const [dirty, setDirty] = useState<Record<CodexUserFileId, boolean>>({
    config: false,
    auth: false,
  });
  const [visualState, setVisualState] = useState<ConfigVisualState>(EMPTY_VISUAL);
  const [visualDirty, setVisualDirty] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [diffOpen, setDiffOpen] = useState(false);
  const [diffOriginal, setDiffOriginal] = useState('');
  const [diffModified, setDiffModified] = useState('');
  const [diffFileName, setDiffFileName] = useState('config.toml');

  const [routeState, setRouteState] = useState<CodexRouteState | null>(null);
  const [routeError, setRouteError] = useState('');
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeSwitching, setRouteSwitching] = useState(false);
  const [selectedRouteTargetId, setSelectedRouteTargetId] = useState('');

  const [searchQuery, setSearchQuery] = useState('');
  const [lastSearchedQuery, setLastSearchedQuery] = useState('');
  const [searchResults, setSearchResults] = useState({ current: 0, total: 0 });

  const [templateKind, setTemplateKind] = useState<TemplateKind>('root');
  const [templateTable, setTemplateTable] = useState('profiles.default');
  const [templateName, setTemplateName] = useState('default');
  const [templateKey, setTemplateKey] = useState('model');
  const [templateValue, setTemplateValue] = useState('gpt-5.5');
  const [templateValueKind, setTemplateValueKind] = useState<TomlValueKind>('string');
  const [jsonPath, setJsonPath] = useState('');
  const [jsonKey, setJsonKey] = useState('OPENAI_API_KEY');
  const [jsonValue, setJsonValue] = useState('');
  const [jsonValueKind, setJsonValueKind] = useState<JsonValueKind>('string');

  const activeSnapshot = snapshots[activeFileId];
  const activeDraft = drafts[activeFileId];
  const activeFileName = activeFileId === 'config' ? 'config.toml' : 'auth.json';
  const activeValidation = activeSnapshot?.validation ?? { isValid: true, message: '尚未同步' };
  const activeDirty =
    activeFileId === 'config' && editorMode === 'visual'
      ? dirty.config || visualDirty
      : dirty[activeFileId];

  const routeOptions = useMemo<SelectOption[]>(
    () => routeState?.targets.map((target) => ({ value: target.id, label: target.label })) ?? [],
    [routeState]
  );
  const resolvedRouteTargetId = resolveSelectedRouteTargetId(routeState, selectedRouteTargetId);
  const selectedRouteTarget = useMemo(
    () => routeState?.targets.find((target) => target.id === resolvedRouteTargetId) ?? null,
    [resolvedRouteTargetId, routeState]
  );
  const routeLabel = routeState
    ? routeState.currentLabel || routeModeLabel(routeState.currentMode)
    : routeError
      ? '检测失败'
      : desktopBridgeAvailable
        ? '检测中'
        : '需要桌面端桥接';

  const syncFiles = useCallback(async () => {
    if (!desktopBridgeAvailable) {
      setError('需要桌面端桥接');
      return;
    }

    setLoading(true);
    setError('');
    try {
      const files = await requestCodexUserFiles();
      const nextSnapshots: SnapshotMap = { config: null, auth: null };
      const nextDrafts: DraftMap = { config: '', auth: '' };
      for (const file of files) {
        nextSnapshots[file.fileId] = file;
        nextDrafts[file.fileId] = file.content;
      }
      setSnapshots(nextSnapshots);
      setDrafts(nextDrafts);
      setDirty({ config: false, auth: false });
      setDiffOpen(false);
      const parsed = parseConfigVisual(nextDrafts.config);
      if (parsed.ok) {
        setVisualState(parsed.value);
        setVisualDirty(false);
      }
    } catch (syncError) {
      const message = syncError instanceof Error ? syncError.message : '同步 Codex 用户配置失败';
      setError(message);
      showNotification(message, 'error');
    } finally {
      setLoading(false);
    }
  }, [desktopBridgeAvailable, showNotification]);

  const syncRoute = useCallback(async () => {
    if (!desktopBridgeAvailable) {
      setRouteError('需要桌面端桥接');
      setRouteState(null);
      return;
    }

    setRouteLoading(true);
    try {
      const state = await requestCodexRouteState();
      setRouteState(state);
      setSelectedRouteTargetId(
        state.currentTargetId || state.targets.find((target) => target.isCurrent)?.id || ''
      );
      setRouteError('');
    } catch (syncError) {
      setRouteState(null);
      setRouteError(syncError instanceof Error ? syncError.message : '读取 Codex 路由失败');
    } finally {
      setRouteLoading(false);
    }
  }, [desktopBridgeAvailable]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void syncRoute();
      void syncFiles();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [syncFiles, syncRoute]);

  useEffect(() => {
    if (activeFileId !== 'auth' || editorMode !== 'visual') return;
    const timer = window.setTimeout(() => {
      setEditorMode('source');
    }, 0);

    return () => window.clearTimeout(timer);
  }, [activeFileId, editorMode]);

  const performSearch = useCallback((query: string, direction: 'next' | 'prev' = 'next') => {
    if (!query || !editorRef.current?.view) return;
    const view = editorRef.current.view;
    const doc = view.state.doc.toString();
    const lowerDoc = doc.toLowerCase();
    const lowerQuery = query.toLowerCase();
    const matches: number[] = [];
    let pos = 0;
    while (pos < lowerDoc.length) {
      const index = lowerDoc.indexOf(lowerQuery, pos);
      if (index === -1) break;
      matches.push(index);
      pos = index + 1;
    }

    if (matches.length === 0) {
      setSearchResults({ current: 0, total: 0 });
      return;
    }

    const cursorPos = direction === 'prev' ? view.state.selection.main.from : view.state.selection.main.to;
    let currentIndex = direction === 'next' ? matches.findIndex((match) => match > cursorPos) : -1;
    if (direction === 'prev') {
      for (let i = matches.length - 1; i >= 0; i -= 1) {
        if (matches[i] < cursorPos) {
          currentIndex = i;
          break;
        }
      }
    }
    if (currentIndex < 0) currentIndex = direction === 'next' ? 0 : matches.length - 1;
    const matchPos = matches[currentIndex];
    setSearchResults({ current: currentIndex + 1, total: matches.length });
    view.dispatch({
      selection: { anchor: matchPos, head: matchPos + query.length },
      scrollIntoView: true,
    });
    view.focus();
  }, []);

  const executeSearch = useCallback(
    (direction: 'next' | 'prev' = 'next') => {
      if (!searchQuery) return;
      setLastSearchedQuery(searchQuery);
      performSearch(searchQuery, direction);
    },
    [performSearch, searchQuery]
  );

  const setActiveDraft = useCallback((value: string) => {
    setDrafts((current) => ({ ...current, [activeFileId]: value }));
    setDirty((current) => ({ ...current, [activeFileId]: true }));
    if (activeFileId === 'config') setVisualDirty(false);
  }, [activeFileId]);

  const materializeVisualDraft = useCallback(() => {
    if (activeFileId !== 'config' || !visualDirty) return activeDraft;
    const next = applyConfigVisual(activeDraft, visualState);
    setDrafts((current) => ({ ...current, config: next }));
    setDirty((current) => ({ ...current, config: true }));
    setVisualDirty(false);
    return next;
  }, [activeDraft, activeFileId, visualDirty, visualState]);

  const handleModeChange = (mode: EditorMode) => {
    if (activeFileId === 'auth' && mode === 'visual') return;
    if (mode === editorMode) return;
    if (mode === 'source') {
      materializeVisualDraft();
      setEditorMode('source');
      return;
    }

    const parsed = parseConfigVisual(activeDraft);
    if (!parsed.ok) {
      showNotification(`TOML 解析失败，已保留源码编辑：${parsed.error}`, 'error');
      setEditorMode('source');
      return;
    }

    setVisualState(parsed.value);
    setVisualDirty(false);
    setEditorMode('visual');
  };

  const buildDraftForSave = () => {
    if (activeFileId === 'config' && editorMode === 'visual') {
      return applyConfigVisual(activeDraft, visualState);
    }
    return activeDraft;
  };

  const handleSave = async () => {
    if (!desktopBridgeAvailable) {
      showNotification('需要桌面端桥接', 'error');
      return;
    }

    setSaving(true);
    try {
      const nextContent = buildDraftForSave();
      const validation = await validateCodexUserFile(activeFileId, nextContent);
      if (!validation.isValid) {
        showNotification(validation.message || '校验失败，未保存', 'error');
        return;
      }

      const original = activeSnapshot?.content ?? '';
      if (original === nextContent) {
        setDirty((current) => ({ ...current, [activeFileId]: false }));
        if (activeFileId === 'config') setVisualDirty(false);
        showNotification('未检测到变更', 'info');
        return;
      }

      setDiffOriginal(original);
      setDiffModified(nextContent);
      setDiffFileName(activeFileName);
      setDiffOpen(true);
    } catch (saveError) {
      showNotification(saveError instanceof Error ? saveError.message : '保存准备失败', 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleConfirmSave = async () => {
    if (!desktopBridgeAvailable) return;
    setSaving(true);
    try {
      const result = await saveCodexUserFile(
        activeFileId,
        diffModified,
        activeSnapshot?.lastWriteTimeUtc ?? null
      );
      setSnapshots((current) => ({ ...current, [activeFileId]: result.snapshot }));
      setDrafts((current) => ({ ...current, [activeFileId]: result.snapshot.content }));
      setDirty((current) => ({ ...current, [activeFileId]: false }));
      if (activeFileId === 'config') {
        const parsed = parseConfigVisual(result.snapshot.content);
        if (parsed.ok) setVisualState(parsed.value);
        setVisualDirty(false);
      }
      setDiffOpen(false);
      showNotification('Codex 用户配置已保存', 'success');
    } catch (saveError) {
      showNotification(saveError instanceof Error ? saveError.message : '保存失败', 'error');
      const latest = await readCodexUserFile(activeFileId).catch(() => null);
      if (latest) setSnapshots((current) => ({ ...current, [activeFileId]: latest }));
    } finally {
      setSaving(false);
    }
  };

  const handleBackup = async () => {
    if (!desktopBridgeAvailable) {
      showNotification('需要桌面端桥接', 'error');
      return;
    }

    setSaving(true);
    try {
      const result = await backupCodexUserFile(activeFileId);
      setSnapshots((current) => ({ ...current, [activeFileId]: result.snapshot }));
      showNotification('备份已创建', 'success');
    } catch (backupError) {
      showNotification(backupError instanceof Error ? backupError.message : '备份失败', 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleRouteSwitch = async () => {
    if (!selectedRouteTarget || selectedRouteTarget.isCurrent || !selectedRouteTarget.canSwitch) return;
    setRouteSwitching(true);
    try {
      const result = await switchCodexRoute(selectedRouteTarget.id);
      setRouteState(result.state);
      setSelectedRouteTargetId(
        result.state.currentTargetId || result.state.targets.find((target) => target.isCurrent)?.id || ''
      );
      setRouteError('');
      await syncFiles();
    } catch (switchError) {
      setRouteError(switchError instanceof Error ? switchError.message : '切换 Codex 路由失败');
      void syncRoute();
    } finally {
      setRouteSwitching(false);
    }
  };

  const buildConfigTemplate = () => {
    if (templateKind === 'root') {
      if (!templateKey.trim()) throw new Error('根参数需要填写键名');
      return `${formatTomlKey(templateKey.trim())} = ${formatTomlValue(templateValue, templateValueKind)}\n`;
    }
    if (templateKind === 'table') {
      if (!templateTable.trim() || !templateKey.trim()) throw new Error('表参数需要填写表名和键名');
      return `[${formatTomlTablePath(templateTable.trim())}]\n${formatTomlKey(templateKey.trim())} = ${formatTomlValue(templateValue, templateValueKind)}\n`;
    }
    if (templateKind === 'profile') {
      if (!templateName.trim()) throw new Error('Profile 模板需要填写名称');
      return `[profiles.${formatTomlKey(templateName.trim())}]\nmodel = ${JSON.stringify(templateValue || 'gpt-5.5')}\nmodel_provider = "openai"\nreasoning_effort = "xhigh"\n`;
    }
    if (!templateName.trim()) throw new Error('Model provider 模板需要填写名称');
    return `[model_providers.${formatTomlKey(templateName.trim())}]\nname = ${JSON.stringify(templateName.trim())}\nbase_url = ${JSON.stringify(templateValue || 'https://api.openai.com/v1')}\nwire_api = "responses"\n`;
  };

  const handleInsertTemplate = async () => {
    if (editorMode !== 'source') {
      showNotification('模板只能在源码编辑中插入', 'error');
      return;
    }

    try {
      let nextContent = activeDraft;
      if (activeFileId === 'config') {
        nextContent = insertSnippet(activeDraft, buildConfigTemplate(), editorRef);
      } else {
        const root = activeDraft.trim() ? JSON.parse(activeDraft) : {};
        if (!root || typeof root !== 'object' || Array.isArray(root)) {
          throw new Error('auth.json 必须是 JSON 对象');
        }
        if (!jsonKey.trim()) throw new Error('JSON 模板需要填写键名');
        let target = root as Record<string, unknown>;
        for (const segment of jsonPath.split('.').map((part) => part.trim()).filter(Boolean)) {
          const current = target[segment];
          if (!current || typeof current !== 'object' || Array.isArray(current)) {
            target[segment] = {};
          }
          target = target[segment] as Record<string, unknown>;
        }
        target[jsonKey.trim()] = parseJsonTemplateValue(jsonValue, jsonValueKind);
        nextContent = `${JSON.stringify(root, null, 2)}\n`;
      }

      const validation = await validateCodexUserFile(activeFileId, nextContent);
      if (!validation.isValid) {
        showNotification(validation.message || '模板生成后校验失败，未插入', 'error');
        return;
      }
      setActiveDraft(nextContent);
      showNotification('模板已插入', 'success');
    } catch (templateError) {
      showNotification(templateError instanceof Error ? templateError.message : '模板插入失败', 'error');
    }
  };

  const updateRoot = <K extends keyof RootVisualState>(key: K, value: RootVisualState[K]) => {
    setVisualState((current) => ({
      ...current,
      root: { ...current.root, [key]: value },
    }));
    setVisualDirty(true);
  };

  const updateProfile = (id: string, patch: Partial<ProfileVisualRow>) => {
    setVisualState((current) => ({
      ...current,
      profiles: current.profiles.map((row) => (row.id === id ? { ...row, ...patch } : row)),
    }));
    setVisualDirty(true);
  };

  const updateProvider = (id: string, patch: Partial<ProviderVisualRow>) => {
    setVisualState((current) => ({
      ...current,
      providers: current.providers.map((row) => (row.id === id ? { ...row, ...patch } : row)),
    }));
    setVisualDirty(true);
  };

  const statusClass = error || !activeValidation.isValid
    ? styles.statusError
    : activeDirty
      ? styles.statusDirty
      : styles.statusValid;
  const statusText = error
    ? '同步失败'
    : loading
      ? '同步中'
      : saving
        ? '处理中'
        : !activeValidation.isValid
          ? '校验失败'
          : activeDirty
            ? '有未保存修改'
            : '已同步';

  if (!desktopBridgeAvailable) {
    return (
      <div className={styles.container}>
        <div className={styles.unavailable}>需要桌面端桥接</div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.pageHeader}>
        <div className={styles.pageHeaderCopy}>
          <span className={styles.pageEyebrow}>用户级 Codex 目录</span>
          <h1 className={styles.pageTitle}>.codex配置</h1>
          <p className={styles.description}>
            只管理用户级 config.toml 与 auth.json，不读取或覆盖项目级 .codex/config.toml。
          </p>
        </div>
        <div className={styles.routePanel}>
          <span className={styles.routeStatus} title={routeState?.statusMessage || routeError}>
            当前：{routeLabel}
          </span>
          <Select
            className={styles.routeSelect}
            value={resolvedRouteTargetId}
            options={routeOptions}
            onChange={setSelectedRouteTargetId}
            placeholder={routeError ? '检测失败' : '选择路由'}
            disabled={routeLoading || routeSwitching || routeOptions.length === 0}
            ariaLabel="Codex 路由目标"
          />
          <Button
            type="button"
            size="sm"
            variant="secondary"
            onClick={handleRouteSwitch}
            disabled={
              routeLoading ||
              routeSwitching ||
              !selectedRouteTarget ||
              selectedRouteTarget.isCurrent ||
              !selectedRouteTarget.canSwitch
            }
            loading={routeSwitching}
          >
            应用
          </Button>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            onClick={() => {
              void syncRoute();
              void syncFiles();
            }}
            disabled={loading || saving}
          >
            <IconRefreshCw size={15} />
            同步
          </Button>
        </div>
      </div>

      <div className={styles.workspace}>
        <div className={styles.fileRail}>
          {FILE_OPTIONS.map((file) => {
            const snapshot = snapshots[file.fileId];
            const isActive = activeFileId === file.fileId;
            return (
              <button
                key={file.fileId}
                type="button"
                className={`${styles.fileButton} ${isActive ? styles.fileButtonActive : ''}`}
                onClick={() => setActiveFileId(file.fileId)}
                disabled={saving}
              >
                <span className={styles.fileName}>{file.name}</span>
                <span className={styles.fileBadge}>{dirty[file.fileId] ? '未保存' : snapshot?.exists ? '已读取' : '缺失'}</span>
                <span className={styles.fileMeta}>
                  {file.label}
                  <br />
                  {snapshot ? `${formatBytes(snapshot.sizeBytes)} · ${formatTimestamp(snapshot.lastWriteTimeUtc)}` : '等待同步'}
                </span>
              </button>
            );
          })}
          {activeSnapshot?.path && <div className={styles.pathLine}>{activeSnapshot.path}</div>}
        </div>

        <div className={styles.mainPanel}>
          <div className={styles.toolbar}>
            <div className={styles.toolbarLeft}>
              <span className={`${styles.statusBadge} ${statusClass}`}>{statusText}</span>
              <div className={styles.modeTabs}>
                {modeOptions.map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    className={`${styles.tabButton} ${editorMode === option.value ? styles.tabButtonActive : ''}`}
                    onClick={() => handleModeChange(option.value as EditorMode)}
                    disabled={activeFileId === 'auth' && option.value === 'visual'}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>
            <div className={styles.toolbarRight}>
              <Button type="button" size="sm" variant="secondary" onClick={handleBackup} disabled={loading || saving || !activeSnapshot?.exists}>
                <IconFileText size={15} />
                备份
              </Button>
              <Button type="button" size="sm" onClick={handleSave} disabled={loading || saving || !activeDirty} loading={saving}>
                <IconCheck size={15} />
                保存
              </Button>
            </div>
          </div>

          <div className={styles.panelBody}>
            {error && <div className="error-box">{error}</div>}
            {!activeValidation.isValid && <div className="error-box">{activeValidation.message}</div>}

            {editorMode === 'visual' && activeFileId === 'config' ? (
              <div className={styles.visualScroll}>
                <div className={styles.visualPanel}>
                  <div className={styles.section}>
                    <div>
                      <h2 className={styles.sectionTitle}>根级常用字段</h2>
                      <div className={styles.sectionHint}>空值会在保存时从对应常用字段中移除，未覆盖字段和高级表会保留。</div>
                    </div>
                    <div className={styles.fieldGrid}>
                      <Input label="profile" value={visualState.root.profile} onChange={(event) => updateRoot('profile', event.target.value)} />
                      <Input label="model" value={visualState.root.model} onChange={(event) => updateRoot('model', event.target.value)} />
                      <Input label="model_provider" value={visualState.root.modelProvider} onChange={(event) => updateRoot('modelProvider', event.target.value)} />
                      <Input label="reasoning_effort" value={visualState.root.reasoningEffort} onChange={(event) => updateRoot('reasoningEffort', event.target.value)} />
                      <Input label="sandbox_mode" value={visualState.root.sandboxMode} onChange={(event) => updateRoot('sandboxMode', event.target.value)} />
                      <Input label="approval_policy" value={visualState.root.approvalPolicy} onChange={(event) => updateRoot('approvalPolicy', event.target.value)} />
                      <Select value={visualState.root.requiresOpenaiAuth} options={[{ value: '', label: '未设置' }, { value: 'true', label: 'true' }, { value: 'false', label: 'false' }]} onChange={(value) => updateRoot('requiresOpenaiAuth', value as '' | 'true' | 'false')} ariaLabel="requires_openai_auth" />
                      <Input label="chatgpt_base_url" value={visualState.root.chatgptBaseUrl} onChange={(event) => updateRoot('chatgptBaseUrl', event.target.value)} />
                      <Input label="cli_auth_credentials_store" value={visualState.root.cliAuthCredentialsStore} onChange={(event) => updateRoot('cliAuthCredentialsStore', event.target.value)} />
                      <Input label="base_url" value={visualState.root.baseUrl} onChange={(event) => updateRoot('baseUrl', event.target.value)} />
                      <Input label="wire_api" value={visualState.root.wireApi} onChange={(event) => updateRoot('wireApi', event.target.value)} />
                    </div>
                  </div>

                  <div className={styles.section}>
                    <div className={styles.sectionHeader}>
                      <div>
                        <h2 className={styles.sectionTitle}>profiles 常用字段</h2>
                        <div className={styles.sectionHint}>对应 [profiles.*] 表。</div>
                      </div>
                      <Button type="button" size="sm" variant="secondary" onClick={() => { setVisualState((current) => ({ ...current, profiles: [...current.profiles, { id: createRowId(), name: 'default', model: '', modelProvider: '', reasoningEffort: '', sandboxMode: '', approvalPolicy: '' }] })); setVisualDirty(true); }}>
                        新增
                      </Button>
                    </div>
                    <div className={styles.tableRows}>
                      {visualState.profiles.length === 0 && <div className={styles.emptyState}>暂无 profile 表</div>}
                      {visualState.profiles.map((row) => (
                        <div key={row.id} className={styles.tableRow}>
                          <Input label="名称" value={row.name} onChange={(event) => updateProfile(row.id, { name: event.target.value })} />
                          <Input label="model" value={row.model} onChange={(event) => updateProfile(row.id, { model: event.target.value })} />
                          <Input label="model_provider" value={row.modelProvider} onChange={(event) => updateProfile(row.id, { modelProvider: event.target.value })} />
                          <Input label="reasoning_effort" value={row.reasoningEffort} onChange={(event) => updateProfile(row.id, { reasoningEffort: event.target.value })} />
                          <Input label="sandbox_mode" value={row.sandboxMode} onChange={(event) => updateProfile(row.id, { sandboxMode: event.target.value })} />
                          <Input label="approval_policy" value={row.approvalPolicy} onChange={(event) => updateProfile(row.id, { approvalPolicy: event.target.value })} />
                          <Button type="button" size="sm" variant="danger" onClick={() => { setVisualState((current) => ({ ...current, profiles: current.profiles.filter((item) => item.id !== row.id) })); setVisualDirty(true); }}>
                            删除
                          </Button>
                        </div>
                      ))}
                    </div>
                  </div>

                  <div className={styles.section}>
                    <div className={styles.sectionHeader}>
                      <div>
                        <h2 className={styles.sectionTitle}>model_providers 常用字段</h2>
                        <div className={styles.sectionHint}>对应 [model_providers.*] 表。</div>
                      </div>
                      <Button type="button" size="sm" variant="secondary" onClick={() => { setVisualState((current) => ({ ...current, providers: [...current.providers, { id: createRowId(), name: 'openai', baseUrl: '', wireApi: '', envKey: '', requiresOpenaiAuth: '' }] })); setVisualDirty(true); }}>
                        新增
                      </Button>
                    </div>
                    <div className={styles.tableRows}>
                      {visualState.providers.length === 0 && <div className={styles.emptyState}>暂无 model provider 表</div>}
                      {visualState.providers.map((row) => (
                        <div key={row.id} className={styles.tableRow}>
                          <Input label="名称" value={row.name} onChange={(event) => updateProvider(row.id, { name: event.target.value })} />
                          <Input label="base_url" value={row.baseUrl} onChange={(event) => updateProvider(row.id, { baseUrl: event.target.value })} />
                          <Input label="wire_api" value={row.wireApi} onChange={(event) => updateProvider(row.id, { wireApi: event.target.value })} />
                          <Input label="env_key" value={row.envKey} onChange={(event) => updateProvider(row.id, { envKey: event.target.value })} />
                          <Select value={row.requiresOpenaiAuth} options={[{ value: '', label: '未设置' }, { value: 'true', label: 'true' }, { value: 'false', label: 'false' }]} onChange={(value) => updateProvider(row.id, { requiresOpenaiAuth: value as '' | 'true' | 'false' })} ariaLabel="requires_openai_auth" />
                          <Button type="button" size="sm" variant="danger" onClick={() => { setVisualState((current) => ({ ...current, providers: current.providers.filter((item) => item.id !== row.id) })); setVisualDirty(true); }}>
                            删除
                          </Button>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              <div className={styles.sourceWorkspace}>
                <div className={styles.sourceToolbar}>
                  <div className={styles.searchInputWrapper}>
                    <Input
                      value={searchQuery}
                      onChange={(event) => {
                        setSearchQuery(event.target.value);
                        setSearchResults({ current: 0, total: 0 });
                      }}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          event.preventDefault();
                          executeSearch(event.shiftKey ? 'prev' : 'next');
                        }
                      }}
                      placeholder="搜索配置内容..."
                      className={styles.searchInput}
                      rightElement={
                        <span className={styles.searchRight}>
                          {searchQuery && lastSearchedQuery === searchQuery && (
                            <span className={styles.searchCount}>
                              {searchResults.total > 0 ? `${searchResults.current} / ${searchResults.total}` : '无结果'}
                            </span>
                          )}
                          <button type="button" className={styles.searchButton} onClick={() => executeSearch('next')} disabled={!searchQuery} title="搜索">
                            <IconSearch size={15} />
                          </button>
                        </span>
                      }
                    />
                  </div>
                  <div className={styles.searchActions}>
                    <Button type="button" size="sm" variant="secondary" onClick={() => performSearch(lastSearchedQuery, 'prev')} disabled={!lastSearchedQuery || searchResults.total === 0} title="上一个">
                      <IconChevronUp size={15} />
                    </Button>
                    <Button type="button" size="sm" variant="secondary" onClick={() => performSearch(lastSearchedQuery, 'next')} disabled={!lastSearchedQuery || searchResults.total === 0} title="下一个">
                      <IconChevronDown size={15} />
                    </Button>
                  </div>
                </div>

                <div className={styles.templatePanel}>
                  {activeFileId === 'config' ? (
                    <div className={styles.templateGrid}>
                      <Select value={templateKind} options={[{ value: 'root', label: '根参数' }, { value: 'table', label: '指定表参数' }, { value: 'profile', label: '配置档模板' }, { value: 'provider', label: '模型提供方模板' }]} onChange={(value) => setTemplateKind(value as TemplateKind)} ariaLabel="模板类型" />
                      {templateKind === 'table' && <Input label="表名" value={templateTable} onChange={(event) => setTemplateTable(event.target.value)} />}
                      {(templateKind === 'profile' || templateKind === 'provider') && <Input label="名称" value={templateName} onChange={(event) => setTemplateName(event.target.value)} />}
                      {(templateKind === 'root' || templateKind === 'table') && <Input label="键名" value={templateKey} onChange={(event) => setTemplateKey(event.target.value)} />}
                      <Input label={templateKind === 'provider' ? 'base_url' : templateKind === 'profile' ? 'model' : '值'} value={templateValue} onChange={(event) => setTemplateValue(event.target.value)} />
                      {(templateKind === 'root' || templateKind === 'table') && <Select value={templateValueKind} options={[{ value: 'string', label: '字符串' }, { value: 'number', label: '数字' }, { value: 'boolean', label: '布尔' }, { value: 'raw', label: '原始 TOML' }]} onChange={(value) => setTemplateValueKind(value as TomlValueKind)} ariaLabel="值类型" />}
                      <Button type="button" size="sm" variant="secondary" onClick={() => void handleInsertTemplate()}>
                        插入模板
                      </Button>
                    </div>
                  ) : (
                    <div className={styles.templateGrid}>
                      <Input label="JSON 路径" value={jsonPath} onChange={(event) => setJsonPath(event.target.value)} placeholder="tokens.openai" />
                      <Input label="键名" value={jsonKey} onChange={(event) => setJsonKey(event.target.value)} />
                      <Input label="值" value={jsonValue} onChange={(event) => setJsonValue(event.target.value)} />
                      <Select value={jsonValueKind} options={[{ value: 'string', label: '字符串' }, { value: 'number', label: '数字' }, { value: 'boolean', label: '布尔' }, { value: 'object', label: '对象/数组' }, { value: 'null', label: '空值' }]} onChange={(value) => setJsonValueKind(value as JsonValueKind)} ariaLabel="JSON 值类型" />
                      <Button type="button" size="sm" variant="secondary" onClick={() => void handleInsertTemplate()}>
                        插入模板
                      </Button>
                    </div>
                  )}
                </div>

                <div className={styles.editorWrapper}>
                  <Suspense fallback={null}>
                    <LazyConfigSourceEditor
                      editorRef={editorRef}
                      value={activeDraft}
                      onChange={setActiveDraft}
                      theme={resolvedTheme}
                      editable={!loading && !saving}
                      placeholder={activeFileId === 'config' ? 'model = "gpt-5.5"' : '{\n  "OPENAI_API_KEY": ""\n}'}
                      language={activeFileId === 'config' ? 'toml' : 'json'}
                    />
                  </Suspense>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      <DiffModal
        open={diffOpen}
        original={diffOriginal}
        modified={diffModified}
        fileName={diffFileName}
        onConfirm={handleConfirmSave}
        onCancel={() => setDiffOpen(false)}
        loading={saving}
      />
    </div>
  );
}
