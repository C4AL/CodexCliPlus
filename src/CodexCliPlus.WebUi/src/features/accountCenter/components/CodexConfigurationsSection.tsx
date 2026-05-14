import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { CodexSection, useProviderStats } from '@/components/providers';
import {
  withDisableAllModelsRule,
  withoutDisableAllModelsRule,
} from '@/components/providers/utils';
import { usePageTransitionLayer } from '@/components/common/PageTransitionLayer';
import { useDesktopDataChanged } from '@/hooks/useDesktopDataChanged';
import { providersApi } from '@/services/api';
import { useAuthStore, useConfigStore, useNotificationStore } from '@/stores';
import type { ProviderKeyConfig } from '@/types';
import { indexUsageDetailsByAuthIndex, indexUsageDetailsBySource } from '@/utils/usageIndex';
import { CodexConfigEditor } from './CodexConfigEditor';
import styles from '@/pages/AiProvidersPage.module.scss';

type CodexEditorState = { index: number | null } | null;

export function CodexConfigurationsSection() {
  const { t } = useTranslation();
  const { showNotification, showConfirmation } = useNotificationStore();
  const connectionStatus = useAuthStore((state) => state.connectionStatus);

  const config = useConfigStore((state) => state.config);
  const fetchConfig = useConfigStore((state) => state.fetchConfig);
  const updateConfigValue = useConfigStore((state) => state.updateConfigValue);
  const clearCache = useConfigStore((state) => state.clearCache);
  const isCacheValid = useConfigStore((state) => state.isCacheValid);

  const hasMounted = useRef(false);
  const editorRef = useRef<HTMLDivElement | null>(null);
  const [loading, setLoading] = useState(() => !isCacheValid());
  const [error, setError] = useState('');
  const [editor, setEditor] = useState<CodexEditorState>(null);

  const [codexConfigs, setCodexConfigs] = useState<ProviderKeyConfig[]>(
    () => config?.codexApiKeys || []
  );

  const [configSwitchingKey, setConfigSwitchingKey] = useState<string | null>(null);

  const disableControls = connectionStatus !== 'connected';
  const isSwitching = Boolean(configSwitchingKey);

  const pageTransitionLayer = usePageTransitionLayer();
  const isCurrentLayer = pageTransitionLayer ? pageTransitionLayer.status === 'current' : true;

  const { keyStats, usageDetails, loadKeyStats, refreshKeyStats } = useProviderStats({
    enabled: isCurrentLayer,
  });
  const usageDetailsBySource = useMemo(
    () => indexUsageDetailsBySource(usageDetails),
    [usageDetails]
  );
  const usageDetailsByAuthIndex = useMemo(
    () => indexUsageDetailsByAuthIndex(usageDetails),
    [usageDetails]
  );

  const getErrorMessage = (err: unknown) => {
    if (err instanceof Error) return err.message;
    if (typeof err === 'string') return err;
    return '';
  };

  const loadConfigs = useCallback(async () => {
    const hasValidCache = isCacheValid();
    if (!hasValidCache) {
      setLoading(true);
    }
    setError('');
    try {
      const data = await fetchConfig();
      setCodexConfigs(data?.codexApiKeys || []);
    } catch (err: unknown) {
      const message = getErrorMessage(err) || t('notification.refresh_failed');
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [fetchConfig, isCacheValid, t]);

  useEffect(() => {
    if (hasMounted.current) return;
    hasMounted.current = true;
    loadConfigs();
  }, [loadConfigs]);

  useEffect(() => {
    if (!isCurrentLayer) return;
    void loadKeyStats().catch(() => {});
  }, [isCurrentLayer, loadKeyStats]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (config?.codexApiKeys) setCodexConfigs(config.codexApiKeys);
    }, 0);

    return () => window.clearTimeout(timer);
  }, [config?.codexApiKeys]);

  useDesktopDataChanged(['providers', 'config', 'usage'], () => {
    void loadConfigs();
    void refreshKeyStats();
  }, isCurrentLayer);

  useEffect(() => {
    if (!editor) return;
    const frame = window.requestAnimationFrame(() => {
      editorRef.current?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    });
    return () => window.cancelAnimationFrame(frame);
  }, [editor]);

  const openEditor = useCallback((index: number | null) => {
    setEditor({ index });
  }, []);

  const closeEditor = useCallback(() => {
    setEditor(null);
  }, []);

  const handleEditorSaved = useCallback(
    (nextList: ProviderKeyConfig[]) => {
      setCodexConfigs(nextList);
      void refreshKeyStats();
    },
    [refreshKeyStats]
  );

  const setConfigEnabled = async (index: number, enabled: boolean) => {
    const current = codexConfigs[index];
    if (!current) return;

    const switchingKey = `codex:${current.apiKey}`;
    setConfigSwitchingKey(switchingKey);

    const previousList = codexConfigs;
    const nextExcluded = enabled
      ? withoutDisableAllModelsRule(current.excludedModels)
      : withDisableAllModelsRule(current.excludedModels);
    const nextItem: ProviderKeyConfig = { ...current, excludedModels: nextExcluded };
    const nextList = previousList.map((item, idx) => (idx === index ? nextItem : item));

    setCodexConfigs(nextList);
    updateConfigValue('codex-api-key', nextList);
    clearCache('codex-api-key');

    try {
      await providersApi.saveCodexConfigs(nextList);
      showNotification(
        enabled ? t('notification.config_enabled') : t('notification.config_disabled'),
        'success'
      );
    } catch (err: unknown) {
      const message = getErrorMessage(err);
      setCodexConfigs(previousList);
      updateConfigValue('codex-api-key', previousList);
      clearCache('codex-api-key');
      showNotification(`${t('notification.update_failed')}: ${message}`, 'error');
    } finally {
      setConfigSwitchingKey(null);
    }
  };

  const deleteCodex = async (index: number) => {
    const entry = codexConfigs[index];
    if (!entry) return;
    showConfirmation({
      title: t('ai_providers.codex_delete_title', { defaultValue: '删除 Codex 配置' }),
      message: t('ai_providers.codex_delete_confirm'),
      variant: 'danger',
      confirmText: t('common.confirm'),
      onConfirm: async () => {
        try {
          await providersApi.deleteCodexConfig(entry.apiKey, entry.baseUrl);
          const next = codexConfigs.filter((_, idx) => idx !== index);
          setCodexConfigs(next);
          updateConfigValue('codex-api-key', next);
          clearCache('codex-api-key');
          showNotification(t('notification.codex_config_deleted'), 'success');
        } catch (err: unknown) {
          const message = getErrorMessage(err);
          showNotification(`${t('notification.delete_failed')}: ${message}`, 'error');
        }
      },
    });
  };

  return (
    <div className={styles.section}>
      {error && <div className="error-box">{error}</div>}

      <CodexSection
        configs={codexConfigs}
        keyStats={keyStats}
        usageDetailsBySource={usageDetailsBySource}
        usageDetailsByAuthIndex={usageDetailsByAuthIndex}
        loading={loading}
        disableControls={disableControls}
        isSwitching={isSwitching}
        onAdd={() => openEditor(null)}
        onEdit={(index) => openEditor(index)}
        onDelete={(index) => void deleteCodex(index)}
        onToggle={(index, enabled) => void setConfigEnabled(index, enabled)}
      />

      {editor && (
        <div ref={editorRef}>
          <CodexConfigEditor
            editIndex={editor.index}
            onClose={closeEditor}
            onSaved={handleEditorSaved}
          />
        </div>
      )}
      </div>
  );
}
