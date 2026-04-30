import type { VisualSection, VisualSectionId } from './VisualConfigEditor.types';
import styles from './VisualConfigEditor.module.scss';

type SectionJumpHandler = (sectionId: VisualSectionId) => void;
type MutableRef<T> = { current: T };

export function VisualSectionNav({
  sections,
  activeSectionId,
  onSectionJump,
}: {
  sections: VisualSection[];
  activeSectionId: VisualSectionId;
  onSectionJump: SectionJumpHandler;
}) {
  return (
    <div className={styles.navList}>
      {sections.map((section, index) => {
        const Icon = section.icon;

        return (
          <button
            key={section.id}
            type="button"
            className={`${styles.navButton} ${
              activeSectionId === section.id ? styles.navButtonActive : ''
            }`}
            onClick={() => onSectionJump(section.id)}
          >
            <span className={styles.navIndex}>{String(index + 1).padStart(2, '0')}</span>
            <span className={styles.navMain}>
              <span className={styles.navHeadingRow}>
                <span className={styles.navLabelWrap}>
                  <span className={styles.navIcon}>
                    <Icon size={14} />
                  </span>
                  <span className={styles.navLabel}>{section.title}</span>
                </span>
                {section.errorCount > 0 ? (
                  <span className={styles.navBadge} aria-hidden="true">
                    {section.errorCount}
                  </span>
                ) : null}
              </span>
              <span className={styles.navDescription}>{section.description}</span>
            </span>
          </button>
        );
      })}
    </div>
  );
}

export function VisualOverview({
  quickJumpLabel,
  validationBlockedLabel,
  hasValidationIssues,
  focusSections,
  activeSectionId,
  onSectionJump,
}: {
  quickJumpLabel: string;
  validationBlockedLabel: string;
  hasValidationIssues: boolean;
  focusSections: VisualSection[];
  activeSectionId: VisualSectionId;
  onSectionJump: SectionJumpHandler;
}) {
  return (
    <div className={styles.overview}>
      <div className={styles.overviewHeader}>
        <div className={styles.overviewMeta}>
          <span className={styles.overviewPill}>{quickJumpLabel}</span>
          {hasValidationIssues ? (
            <span className={`${styles.overviewPill} ${styles.overviewPillWarning}`}>
              {validationBlockedLabel}
            </span>
          ) : null}
        </div>
      </div>

      <div className={styles.overviewFocusList}>
        {focusSections.map((section) => {
          const Icon = section.icon;

          return (
            <button
              key={section.id}
              type="button"
              className={`${styles.overviewFocusLink} ${
                activeSectionId === section.id ? styles.overviewFocusLinkActive : ''
              }`}
              onClick={() => onSectionJump(section.id)}
            >
              <span className={styles.focusIcon}>
                <Icon size={16} />
              </span>
              <span className={styles.focusCopy}>
                <span className={styles.focusTitle}>{section.title}</span>
                <span className={styles.focusDescription}>{section.description}</span>
              </span>
              {section.errorCount > 0 ? (
                <span className={styles.navBadge} aria-hidden="true">
                  {section.errorCount}
                </span>
              ) : null}
            </button>
          );
        })}
      </div>
    </div>
  );
}

export function VisualMobileSectionNav({
  label,
  sections,
  activeSectionId,
  onSectionJump,
  scrollerRef,
  buttonRefs,
}: {
  label: string;
  sections: VisualSection[];
  activeSectionId: VisualSectionId;
  onSectionJump: SectionJumpHandler;
  scrollerRef: MutableRef<HTMLDivElement | null>;
  buttonRefs: MutableRef<Partial<Record<VisualSectionId, HTMLButtonElement | null>>>;
}) {
  return (
    <div className={styles.mobileSectionNav}>
      <div ref={scrollerRef} className={styles.mobileSectionNavScroller} aria-label={label}>
        {sections.map((section, index) => (
          <button
            key={section.id}
            ref={(node) => {
              buttonRefs.current[section.id] = node;
            }}
            type="button"
            className={`${styles.mobileSectionNavButton} ${
              activeSectionId === section.id ? styles.mobileSectionNavButtonActive : ''
            }`}
            onClick={() => onSectionJump(section.id)}
          >
            <span className={styles.mobileSectionNavIndex}>
              {String(index + 1).padStart(2, '0')}
            </span>
            <span className={styles.mobileSectionNavLabel}>{section.title}</span>
            {section.errorCount > 0 ? (
              <span className={styles.mobileSectionNavBadge} aria-hidden="true">
                {section.errorCount}
              </span>
            ) : null}
          </button>
        ))}
      </div>
    </div>
  );
}
