import {
  IconCode,
  IconDiamond,
  IconKey,
  IconSatellite,
  IconSettings,
  IconShield,
  IconTimer,
  IconTrendingUp,
} from '@/components/ui/icons';
import type {
  VisualConfigFieldPath,
  VisualConfigValidationErrors,
} from '@/types/visualConfig';
import type { TranslationFn, VisualSection } from './VisualConfigEditor.types';

export function buildVisualSections(
  t: TranslationFn,
  validationErrors: VisualConfigValidationErrors | undefined,
  hasPayloadValidationErrors: boolean
): VisualSection[] {
  const countErrors = (fields: VisualConfigFieldPath[]) =>
    fields.reduce((total, field) => total + (validationErrors?.[field] ? 1 : 0), 0);

  return [
    {
      id: 'server',
      title: t('config_management.visual.sections.server.title'),
      description: t('config_management.visual.sections.server.description'),
      icon: IconSettings,
      errorCount: countErrors(['port']),
    },
    {
      id: 'tls',
      title: t('config_management.visual.sections.tls.title'),
      description: t('config_management.visual.sections.tls.description'),
      icon: IconShield,
      errorCount: 0,
    },
    {
      id: 'remote',
      title: t('config_management.visual.sections.remote.title'),
      description: t('config_management.visual.sections.remote.description'),
      icon: IconSatellite,
      errorCount: 0,
    },
    {
      id: 'auth',
      title: t('config_management.visual.sections.auth.title'),
      description: t('config_management.visual.sections.auth.description'),
      icon: IconKey,
      errorCount: 0,
    },
    {
      id: 'system',
      title: t('config_management.visual.sections.system.title'),
      description: t('config_management.visual.sections.system.description'),
      icon: IconDiamond,
      errorCount: countErrors(['logsMaxTotalSizeMb']),
    },
    {
      id: 'network',
      title: t('config_management.visual.sections.network.title'),
      description: t('config_management.visual.sections.network.description'),
      icon: IconTrendingUp,
      errorCount: countErrors(['requestRetry', 'maxRetryCredentials', 'maxRetryInterval']),
    },
    {
      id: 'quota',
      title: t('config_management.visual.sections.quota.title'),
      description: t('config_management.visual.sections.quota.description'),
      icon: IconTimer,
      errorCount: 0,
    },
    {
      id: 'streaming',
      title: t('config_management.visual.sections.streaming.title'),
      description: t('config_management.visual.sections.streaming.description'),
      icon: IconSatellite,
      errorCount: countErrors([
        'streaming.keepaliveSeconds',
        'streaming.bootstrapRetries',
        'streaming.nonstreamKeepaliveInterval',
      ]),
    },
    {
      id: 'payload',
      title: t('config_management.visual.sections.payload.title'),
      description: t('config_management.visual.sections.payload.description'),
      icon: IconCode,
      errorCount: hasPayloadValidationErrors ? 1 : 0,
    },
  ];
}

export const getFocusSections = (sections: VisualSection[]) =>
  sections.filter((section) => ['server', 'network', 'payload'].includes(section.id));
