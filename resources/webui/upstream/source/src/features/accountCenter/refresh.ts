import { getManagementAccessBlockedMessage } from '@/utils/managementAccess';

export type AccountRefreshStep = {
  id: string;
  run: () => Promise<void>;
};

export type AccountRefreshResult = {
  stopped: boolean;
  error?: unknown;
};

export {
  getManagementAccessBlockedMessage,
  isManagementAccessBlockedError,
} from '@/utils/managementAccess';

export async function runAccountRefreshSteps(
  steps: AccountRefreshStep[],
  onBlocked?: (message: string, error: unknown) => void
): Promise<AccountRefreshResult> {
  for (const step of steps) {
    try {
      await step.run();
    } catch (error: unknown) {
      const message = getManagementAccessBlockedMessage(error);
      if (!message) {
        continue;
      }

      onBlocked?.(message, error);
      return { stopped: true, error };
    }
  }

  return { stopped: false };
}
