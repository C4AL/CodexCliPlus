import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

const readSource = (relativePath: string) =>
  readFileSync(join(process.cwd(), relativePath), 'utf8');

describe('desktop refresh contracts', () => {
  it('does not refresh usage from persistence-only desktop change scopes', () => {
    const usagePage = readSource('src/pages/UsagePage.tsx');
    const mainLayout = readSource('src/components/layout/MainLayout.tsx');

    expect(usagePage).toContain("useDesktopDataChanged(['usage']");
    expect(usagePage).not.toContain("['usage', 'persistence']");
    expect(mainLayout).toContain("if (scopeSet.has('usage'))");
    expect(mainLayout).not.toContain("scopeSet.has('usage') || scopeSet.has('persistence')");
  });

  it('keeps dashboard auth/quota/provider changes on file and quota refresh paths', () => {
    const dashboardOverview = readSource('src/pages/DashboardOverviewPage.tsx');
    const dataChangedIndex = dashboardOverview.indexOf(
      "useDesktopDataChanged(\n    ['auth-files', 'quota', 'providers']"
    );
    expect(dataChangedIndex).toBeGreaterThanOrEqual(0);

    const dataChangedBlock = dashboardOverview.slice(dataChangedIndex, dataChangedIndex + 420);
    expect(dataChangedBlock).toContain('loadOverviewFiles()');
    expect(dataChangedBlock).toContain('loadCodexQuota');
    expect(dataChangedBlock).not.toContain('loadUsage');
  });
});
