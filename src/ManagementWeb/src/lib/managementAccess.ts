export const MANAGEMENT_ACCESS_BLOCKED_MESSAGE =
  '管理接口认证失败或已被临时封禁，请确认安全密钥未变化；如果后端已封禁，请等待约 30 分钟后重试。';

const isRecord = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object';

const readStatus = (error: unknown): number | undefined => {
  if (!isRecord(error)) return undefined;
  const status = error.status;
  return typeof status === 'number' ? status : undefined;
};

const stringifyErrorDetails = (value: unknown): string => {
  if (typeof value === 'string') return value;
  if (value instanceof Error) {
    const errorWithCause = value as Error & { cause?: unknown };
    return [value.message, stringifyErrorDetails(errorWithCause.cause)].filter(Boolean).join('\n');
  }
  if (!isRecord(value)) return '';

  const details: string[] = [];
  if (typeof value.message === 'string') details.push(value.message);
  if (typeof value.error === 'string') details.push(value.error);
  if (typeof value.detail === 'string') details.push(value.detail);
  if (typeof value.details === 'string') details.push(value.details);
  if (typeof value.data === 'string') details.push(value.data);
  details.push(stringifyErrorDetails(value.cause));

  try {
    details.push(JSON.stringify(value.details ?? value.data ?? value));
  } catch {
    // 忽略循环引用对象，只用已收集的错误文本。
  }

  return details.filter(Boolean).join('\n');
};

export const isManagementAccessBlockedError = (error: unknown): boolean => {
  const status = readStatus(error);
  if (status === 401 || status === 403) {
    return true;
  }

  const text = stringifyErrorDetails(error);
  return /(?:封禁|临时禁止|已禁止|ban(?:ned)?|blocked|forbidden|unauthori[sz]ed|too many|30\s*(?:分钟|minute|min))/i.test(
    text
  );
};

export const getManagementAccessBlockedMessage = (error: unknown): string | null =>
  isManagementAccessBlockedError(error) ? MANAGEMENT_ACCESS_BLOCKED_MESSAGE : null;
