import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { SelectionCheckbox } from '@/components/ui/SelectionCheckbox';
import { AutocompleteInput } from '@/components/ui/AutocompleteInput';
import { EmptyState } from '@/components/ui/EmptyState';
import { IconInfo } from '@/components/ui/icons';
import { useAuthStore, useNotificationStore } from '@/stores';
import { authFilesApi } from '@/services/api';
import styles from '@/pages/AuthFilesOAuthExcludedEditPage.module.scss';

type AuthFileModelItem = { id: string; display_name?: string; type?: string; owned_by?: string };

const OAUTH_PROVIDER_PRESETS = ['codex'];

const normalizeProviderKey = (value: string) => value.trim().toLowerCase();

function filterCodexRecord<T>(record: Record<string, T>): Record<string, T> {
  return Object.fromEntries(
    Object.entries(record).filter(([provider]) => normalizeProviderKey(provider) === 'codex')
  );
}

interface OAuthExcludedEditorProps {
  onClose: () => void;
  onSaved?: () => void;
}

export function OAuthExcludedEditor({ onClose, onSaved }: OAuthExcludedEditorProps) {
  const { t } = useTranslation();
  const { showNotification } = useNotificationStore();
  const connectionStatus = useAuthStore((state) => state.connectionStatus);
  const disableControls = connectionStatus !== 'connected';

  const providerFromParams = 'codex';

  const [provider, setProvider] = useState(providerFromParams);
  const [excluded, setExcluded] = useState<Record<string, string[]>>({});
  const [initialLoading, setInitialLoading] = useState(true);
  const [excludedUnsupported, setExcludedUnsupported] = useState(false);

  const [selectedModels, setSelectedModels] = useState<Set<string>>(new Set());
  const [modelsList, setModelsList] = useState<AuthFileModelItem[]>([]);
  const [modelsLoading, setModelsLoading] = useState(false);
  const [modelsError, setModelsError] = useState<'unsupported' | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setProvider(providerFromParams);
    }, 0);

    return () => window.clearTimeout(timer);
  }, [providerFromParams]);

  const providerOptions = useMemo(() => OAUTH_PROVIDER_PRESETS, []);

  const getTypeLabel = useCallback(
    (type: string): string => {
      const key = `auth_files.filter_${type}`;
      const translated = t(key);
      if (translated !== key) return translated;
      if (type.toLowerCase() === 'iflow') return 'iFlow';
      return type.charAt(0).toUpperCase() + type.slice(1);
    },
    [t]
  );

  const resolvedProviderKey = useMemo(() => normalizeProviderKey(provider), [provider]);
  const isEditing = useMemo(() => {
    if (!resolvedProviderKey) return false;
    return Object.prototype.hasOwnProperty.call(excluded, resolvedProviderKey);
  }, [excluded, resolvedProviderKey]);

  const title = useMemo(() => {
    if (isEditing) {
      return t('oauth_excluded.edit_title', { provider: provider.trim() || resolvedProviderKey });
    }
    return t('oauth_excluded.add_title');
  }, [isEditing, provider, resolvedProviderKey, t]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setInitialLoading(true);
      setExcludedUnsupported(false);
      try {
        const excludedResult = await authFilesApi.getOauthExcludedModels();

        if (cancelled) return;

        setExcluded(filterCodexRecord(excludedResult ?? {}));
      } catch (err: unknown) {
        const status =
          typeof err === 'object' && err !== null && 'status' in err
            ? (err as { status?: unknown }).status
            : undefined;

        if (status === 404) {
          setExcludedUnsupported(true);
        }
      } finally {
        if (!cancelled) {
          setInitialLoading(false);
        }
      }
    };

    load().catch(() => {
      if (!cancelled) {
        setInitialLoading(false);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!resolvedProviderKey) {
      const timer = window.setTimeout(() => {
        setSelectedModels(new Set());
      }, 0);

      return () => window.clearTimeout(timer);
    }

    const timer = window.setTimeout(() => {
      const existing = excluded[resolvedProviderKey] ?? [];
      setSelectedModels(new Set(existing));
    }, 0);

    return () => window.clearTimeout(timer);
  }, [excluded, resolvedProviderKey]);

  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      if (!resolvedProviderKey || excludedUnsupported) {
        setModelsList([]);
        setModelsError(null);
        setModelsLoading(false);
        return;
      }

      setModelsLoading(true);
      setModelsError(null);

      authFilesApi
        .getModelDefinitions(resolvedProviderKey)
        .then((models) => {
          if (cancelled) return;
          setModelsList(models);
        })
        .catch((err: unknown) => {
          if (cancelled) return;
          const status =
            typeof err === 'object' && err !== null && 'status' in err
              ? (err as { status?: unknown }).status
              : undefined;

          if (status === 404) {
            setModelsList([]);
            setModelsError('unsupported');
            return;
          }

          const errorMessage = err instanceof Error ? err.message : '';
          showNotification(`${t('notification.load_failed')}: ${errorMessage}`, 'error');
        })
        .finally(() => {
          if (cancelled) return;
          setModelsLoading(false);
        });
    }, 0);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [excludedUnsupported, resolvedProviderKey, showNotification, t]);

  const updateProvider = useCallback(
    (_value: string) => {
      setProvider('codex');
    },
    []
  );

  const toggleModel = useCallback((modelId: string, checked: boolean) => {
    setSelectedModels((prev) => {
      const next = new Set(prev);
      if (checked) {
        next.add(modelId);
      } else {
        next.delete(modelId);
      }
      return next;
    });
  }, []);

  const handleSave = useCallback(async () => {
    const normalizedProvider = normalizeProviderKey(provider);
    if (!normalizedProvider) {
      showNotification(t('oauth_excluded.provider_required'), 'error');
      return;
    }

    const models = [...selectedModels];
    setSaving(true);
    try {
      if (models.length) {
        await authFilesApi.saveOauthExcludedModels(normalizedProvider, models);
      } else {
        await authFilesApi.deleteOauthExcludedEntry(normalizedProvider);
      }
      showNotification(t('oauth_excluded.save_success'), 'success');
      onSaved?.();
      onClose();
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : '';
      showNotification(`${t('oauth_excluded.save_failed')}: ${errorMessage}`, 'error');
    } finally {
      setSaving(false);
    }
  }, [onClose, onSaved, provider, selectedModels, showNotification, t]);

  const canSave = !disableControls && !saving && !initialLoading && !excludedUnsupported;

  return (
    <div className={styles.pageContent}>
      <Card
        title={title}
        extra={
          <div className={styles.headerActions}>
            <Button variant="secondary" size="sm" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button size="sm" onClick={handleSave} loading={saving} disabled={!canSave}>
              {t('oauth_excluded.save')}
            </Button>
          </div>
        }
      >
        {initialLoading && <div className="hint">{t('common.loading')}</div>}
      </Card>
      {initialLoading ? null : excludedUnsupported ? (
        <Card>
          <EmptyState
            title={t('oauth_excluded.upgrade_required_title')}
            description={t('oauth_excluded.upgrade_required_desc')}
          />
        </Card>
      ) : (
        <>
          <Card className={styles.settingsCard}>
            <div className={styles.settingsHeader}>
              <div className={styles.settingsHeaderTitle}>
                <IconInfo size={16} />
                <span>{t('oauth_excluded.title')}</span>
              </div>
              <div className={styles.settingsHeaderHint}>{t('oauth_excluded.description')}</div>
            </div>

            <div className={styles.settingsSection}>
              <div className={styles.settingsRow}>
                <div className={styles.settingsInfo}>
                  <div className={styles.settingsLabel}>{t('oauth_excluded.provider_label')}</div>
                  <div className={styles.settingsDesc}>{t('oauth_excluded.provider_hint')}</div>
                </div>
                <div className={styles.settingsControl}>
                  <AutocompleteInput
                    id="oauth-excluded-provider"
                    placeholder={t('oauth_excluded.provider_placeholder')}
                    value={provider}
                    onChange={updateProvider}
                    options={providerOptions}
                    disabled={disableControls || saving}
                    wrapperStyle={{ marginBottom: 0 }}
                  />
                </div>
              </div>

              {providerOptions.length > 0 && (
                <div className={styles.tagList}>
                  {providerOptions.map((option) => {
                    const isActive = normalizeProviderKey(provider) === option.toLowerCase();
                    return (
                      <button
                        key={option}
                        type="button"
                        className={`${styles.tag} ${isActive ? styles.tagActive : ''}`}
                        onClick={() => updateProvider(option)}
                        disabled={disableControls || saving}
                      >
                        {getTypeLabel(option)}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          </Card>

          <Card className={styles.settingsCard}>
            <div className={styles.settingsHeader}>
              <div className={styles.settingsHeaderTitle}>{t('oauth_excluded.models_label')}</div>
              {resolvedProviderKey && (
                <div className={styles.modelsHint}>
                  {modelsLoading ? (
                    <>
                      <LoadingSpinner size={14} />
                      <span>{t('oauth_excluded.models_loading')}</span>
                    </>
                  ) : modelsError === 'unsupported' ? (
                    <span>{t('oauth_excluded.models_unsupported')}</span>
                  ) : modelsList.length > 0 ? (
                    <span>{t('oauth_excluded.models_loaded', { count: modelsList.length })}</span>
                  ) : (
                    <span>{t('oauth_excluded.no_models_available')}</span>
                  )}
                </div>
              )}
            </div>

            {modelsLoading ? (
              <div className={styles.loadingModels}>
                <LoadingSpinner size={16} />
                <span>{t('common.loading')}</span>
              </div>
            ) : modelsList.length > 0 ? (
              <div className={styles.modelList}>
                {modelsList.map((model) => {
                  const checked = selectedModels.has(model.id);
                  return (
                    <SelectionCheckbox
                      key={model.id}
                      checked={checked}
                      disabled={disableControls || saving}
                      onChange={(value) => toggleModel(model.id, value)}
                      className={styles.modelItem}
                      labelClassName={styles.modelText}
                      label={
                        <>
                          <span className={styles.modelId}>{model.id}</span>
                          {model.display_name && model.display_name !== model.id && (
                            <span className={styles.modelDisplayName}>{model.display_name}</span>
                          )}
                        </>
                      }
                    />
                  );
                })}
              </div>
            ) : resolvedProviderKey ? (
              <div className={styles.emptyModels}>
                {modelsError === 'unsupported'
                  ? t('oauth_excluded.models_unsupported')
                  : t('oauth_excluded.no_models_available')}
              </div>
            ) : (
              <div className={styles.emptyModels}>{t('oauth_excluded.provider_required')}</div>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
