import type { ComponentType } from 'react';
import type { useTranslation } from 'react-i18next';
import type { IconProps } from '@/components/ui/icons';

export type VisualSectionId =
  | 'server'
  | 'tls'
  | 'remote'
  | 'auth'
  | 'system'
  | 'network'
  | 'quota'
  | 'streaming'
  | 'payload';

export type VisualSection = {
  id: VisualSectionId;
  title: string;
  description: string;
  icon: ComponentType<IconProps>;
  errorCount: number;
};

export type TranslationFn = ReturnType<typeof useTranslation>['t'];
