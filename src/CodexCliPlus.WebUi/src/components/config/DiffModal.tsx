import { Fragment, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Decoration, Diff, Hunk } from 'react-diff-view';
import 'react-diff-view/style/index.css';
import { Modal } from '@/components/ui/Modal';
import { Button } from '@/components/ui/Button';
import { IconChevronDown, IconFileText } from '@/components/ui/icons';
import { buildConfigDiff } from '@/utils/configDiff';
import styles from './DiffModal.module.scss';

type DiffModalProps = {
  open: boolean;
  original: string;
  modified: string;
  fileName?: string;
  onConfirm: () => void;
  onCancel: () => void;
  loading?: boolean;
};

const STAT_BLOCKS = 5;

function StatBar({ additions, deletions }: { additions: number; deletions: number }) {
  const total = additions + deletions;
  if (total === 0) return null;
  const addBlocks = Math.round((additions / total) * STAT_BLOCKS);
  return (
    <span className={styles.statBar}>
      {Array.from({ length: STAT_BLOCKS }, (_, i) => (
        <span
          key={i}
          className={`${styles.statBlock} ${i < addBlocks ? styles.statBlockAdd : styles.statBlockDel}`}
        />
      ))}
    </span>
  );
}

export function DiffModal({
  open,
  original,
  modified,
  fileName = 'config.yaml',
  onConfirm,
  onCancel,
  loading = false
}: DiffModalProps) {
  const { t } = useTranslation();

  const diff = useMemo(() => buildConfigDiff(original, modified), [original, modified]);

  return (
    <Modal
      open={open}
      title={t('config_management.diff.title')}
      onClose={onCancel}
      width="min(1200px, 90vw)"
      className={styles.diffModal}
      closeDisabled={loading}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={loading}>
            {t('common.cancel')}
          </Button>
          <Button onClick={onConfirm} loading={loading} disabled={loading}>
            {t('config_management.diff.confirm')}
          </Button>
        </>
      }
    >
      <div className={styles.content}>
        {diff.hunks.length === 0 ? (
          <div className={styles.emptyState}>{t('config_management.diff.no_changes')}</div>
        ) : (
          <div className={styles.diffContainer}>
            <div className={styles.fileHeader}>
              <IconFileText className={styles.fileIcon} size={16} />
              <span className={styles.fileName}>{fileName}</span>
              <span className={styles.fileStats}>
                <span className={styles.statAdditions}>+{diff.additions}</span>
                <span className={styles.statDeletions}>-{diff.deletions}</span>
                <StatBar additions={diff.additions} deletions={diff.deletions} />
              </span>
            </div>

            <div className={styles.diffBody}>
              {diff.file ? (
                <Diff
                  className={styles.diffTable}
                  viewType="unified"
                  diffType={diff.file.type}
                  hunks={diff.hunks}
                >
                  {(hunks) =>
                    hunks.map((hunk) => (
                      <Fragment key={`${hunk.oldStart}-${hunk.newStart}-${hunk.content}`}>
                        <Decoration contentClassName={styles.hunkDecorationCell}>
                          <div className={styles.hunkHeader}>
                            <span className={styles.hunkGutter}>
                              <IconChevronDown className={styles.hunkExpandIcon} size={12} />
                            </span>
                            <span className={styles.hunkGutter} />
                            <span className={styles.hunkText}>{hunk.content}</span>
                          </div>
                        </Decoration>
                        <Hunk hunk={hunk} />
                      </Fragment>
                    ))
                  }
                </Diff>
              ) : null}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
