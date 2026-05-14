import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation } from 'react-router-dom';
import { AuthFilesSection } from '@/features/accountCenter/components/AuthFilesSection';
import { CodexConfigurationsSection } from '@/features/accountCenter/components/CodexConfigurationsSection';
import { OAuthLoginSection } from '@/features/accountCenter/components/OAuthLoginSection';
import { QuotaManagementSection } from '@/features/accountCenter/components/QuotaManagementSection';
import styles from './AccountCenterPage.module.scss';

const SECTION_IDS = {
  oauth: 'oauth-login',
  codex: 'codex-config',
  authFiles: 'auth-files',
  quota: 'quota-management',
} as const;

const normalizeAccountHash = (hash: string) => {
  const value = hash.replace(/^#/, '').trim();
  if (!value) return SECTION_IDS.oauth;
  if (value === 'oauth') return SECTION_IDS.oauth;
  if (value === 'codex') return SECTION_IDS.codex;
  if (value === 'auth') return SECTION_IDS.authFiles;
  if (value === 'quota') return SECTION_IDS.quota;
  return Object.values(SECTION_IDS).includes(value as (typeof SECTION_IDS)[keyof typeof SECTION_IDS])
    ? value
    : SECTION_IDS.oauth;
};

export function AccountCenterPage() {
  const { t } = useTranslation();
  const location = useLocation();

  useEffect(() => {
    if (!location.hash) return;
    const sectionId = normalizeAccountHash(location.hash);
    const frame = window.requestAnimationFrame(() => {
      document.getElementById(sectionId)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    });
    return () => window.cancelAnimationFrame(frame);
  }, [location.hash]);

  return (
    <div className={styles.container}>
      <div className={styles.pageHeader}>
        <h1 className={styles.pageTitle}>{t('account_center.title')}</h1>
        <p className={styles.description}>{t('account_center.description')}</p>
      </div>

      <section id={SECTION_IDS.oauth} className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2>{t('account_center.oauth_section')}</h2>
        </div>
        <OAuthLoginSection />
      </section>

      <section id={SECTION_IDS.codex} className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2>{t('account_center.codex_section')}</h2>
        </div>
        <CodexConfigurationsSection />
      </section>

      <section id={SECTION_IDS.authFiles} className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2>{t('account_center.auth_files_section')}</h2>
        </div>
        <AuthFilesSection />
      </section>

      <section id={SECTION_IDS.quota} className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2>{t('account_center.quota_section')}</h2>
        </div>
        <QuotaManagementSection />
      </section>
    </div>
  );
}
