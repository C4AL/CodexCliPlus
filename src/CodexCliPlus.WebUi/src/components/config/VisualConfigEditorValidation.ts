import type {
  PayloadParamValidationErrorCode,
  VisualConfigValidationErrorCode,
} from '@/types/visualConfig';
import type { TranslationFn } from './VisualConfigEditor.types';

export function getValidationMessage(
  t: TranslationFn,
  errorCode?: VisualConfigValidationErrorCode | PayloadParamValidationErrorCode
) {
  if (!errorCode) return undefined;
  return t(`config_management.visual.validation.${errorCode}`);
}
