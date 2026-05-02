import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
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
  const navigate = useNavigate();
  const activeSectionId = normalizeAccountHash(location.hash);

  const sections = useMemo(
    () => [
      { id: SECTION_IDS.oauth, label: t('account_center.oauth_section') },
      { id: SECTION_IDS.codex, label: t('account_center.codex_section') },
      { id: SECTION_IDS.authFiles, label: t('account_center.auth_files_section') },
      { id: SECTION_IDS.quota, label: t('account_center.quota_section') },
    ],
    [t]
  );

  useEffect(() => {
    if (!location.hash) return;
    const sectionId = normalizeAccountHash(location.hash);
    const frame = window.requestAnimationFrame(() => {
      document.getElementById(sectionId)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    });
    return () => window.cancelAnimationFrame(frame);
  }, [location.hash]);

  const goToSection = (id: string) => {
    navigate({ pathname: '/accounts', hash: `#${id}` }, { replace: false });
  };

  return (
    <div className={styles.container}>
      <div className={styles.pageHeader}>
        <h1 className={styles.pageTitle}>{t('account_center.title')}</h1>
        <p className={styles.description}>{t('account_center.description')}</p>
      </div>

      <div className={styles.segmentNav} role="tablist" aria-label={t('account_center.title')}>
        {sections.map((section) => (
          <button
            key={section.id}
            type="button"
            role="tab"
            aria-selected={activeSectionId === section.id}
            className={`${styles.segmentButton} ${
              activeSectionId === section.id ? styles.segmentButtonActive : ''
            }`}
            onClick={() => goToSection(section.id)}
          >
            {section.label}
          </button>
        ))}
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
