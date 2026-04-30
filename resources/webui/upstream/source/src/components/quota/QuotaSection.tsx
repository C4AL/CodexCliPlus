/**
 * Generic quota section component.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { useQuotaStore, useThemeStore } from '@/stores';
import type { AuthFileItem, ResolvedTheme } from '@/types';
import { QuotaCard } from './QuotaCard';
import type { QuotaStatusState } from './QuotaCard';
import { useQuotaLoader } from './useQuotaLoader';
import type { QuotaConfig } from './quotaConfigs';
import { useGridColumns } from './useGridColumns';
import styles from '@/pages/QuotaPage.module.scss';

type QuotaUpdater<T> = T | ((prev: T) => T);

type QuotaSetter<T> = (updater: QuotaUpdater<T>) => void;

type QuotaScope = 'page' | 'all';

interface QuotaSectionProps<TState extends QuotaStatusState, TData> {
  config: QuotaConfig<TState, TData>;
  files: AuthFileItem[];
  loading: boolean;
  disabled: boolean;
}

export function QuotaSection<TState extends QuotaStatusState, TData>({
  config,
  files,
  loading,
  disabled
}: QuotaSectionProps<TState, TData>) {
  const { t } = useTranslation();
  const resolvedTheme: ResolvedTheme = useThemeStore((state) => state.resolvedTheme);
  const setQuota = useQuotaStore((state) => state[config.storeSetter]) as QuotaSetter<
    Record<string, TState>
  >;

  const [, gridRef] = useGridColumns(380);
  const [, setSectionLoading] = useState(false);
  const lastLoadedFilesKeyRef = useRef('');

  const filteredFiles = useMemo(
    () => files.filter((file) => config.filterFn(file)),
    [files, config]
  );

  const { quota, loadQuota } = useQuotaLoader(config);

  const setLoading = useCallback((isLoading: boolean, _scope?: QuotaScope | null) => {
    setSectionLoading(isLoading);
  }, []);

  const runQuotaRefresh = useCallback(
    async (targets: AuthFileItem[]) => {
      if (targets.length === 0) return;
      await loadQuota(targets, 'all', setLoading);
    },
    [loadQuota, setLoading]
  );

  useEffect(() => {
    if (loading || disabled) return;
    const filesKey = filteredFiles.map((file) => `${file.name}:${file.disabled ? '0' : '1'}`).join('|');
    if (!filesKey || filesKey === lastLoadedFilesKeyRef.current) return;
    lastLoadedFilesKeyRef.current = filesKey;
    void runQuotaRefresh(filteredFiles.filter((file) => !file.disabled));
  }, [disabled, filteredFiles, loading, runQuotaRefresh]);

  useEffect(() => {
    if (loading) return;
    if (filteredFiles.length === 0) {
      setQuota({});
      return;
    }
    setQuota((prev) => {
      const nextState: Record<string, TState> = {};
      filteredFiles.forEach((file) => {
        const cached = prev[file.name];
        if (cached) {
          nextState[file.name] = cached;
        }
      });
      return nextState;
    });
  }, [filteredFiles, loading, setQuota]);

  const titleNode = (
    <div className={styles.titleWrapper}>
      <span>{t(`${config.i18nPrefix}.title`)}</span>
      {filteredFiles.length > 0 && <span className={styles.countBadge}>{filteredFiles.length}</span>}
    </div>
  );

  return (
    <Card title={titleNode}>
      {filteredFiles.length === 0 ? (
        <EmptyState
          title={t(`${config.i18nPrefix}.empty_title`)}
          description={t(`${config.i18nPrefix}.empty_desc`)}
        />
      ) : (
        <div ref={gridRef} className={config.gridClassName}>
          {filteredFiles.map((item) => (
            <QuotaCard
              key={item.name}
              item={item}
              quota={quota[item.name]}
              resolvedTheme={resolvedTheme}
              i18nPrefix={config.i18nPrefix}
              cardIdleMessageKey={config.cardIdleMessageKey}
              cardClassName={config.cardClassName}
              defaultType={config.type}
              renderQuotaItems={config.renderQuotaItems}
            />
          ))}
        </div>
      )}
    </Card>
  );
}
