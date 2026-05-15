import type { ProviderKeyConfig } from '@/types';
import type { HeaderEntry } from '@/lib/headers';

export interface ModelEntry {
  name: string;
  alias: string;
}

export type ProviderFormState = Omit<ProviderKeyConfig, 'headers'> & {
  headers: HeaderEntry[];
  modelEntries: ModelEntry[];
  excludedText: string;
};
