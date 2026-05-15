import { FILE_HEADERS_ONLY, createTwoFilesPatch } from 'diff';
import {
  isDelete,
  isInsert,
  parseDiff,
  type FileData,
  type HunkData,
} from 'react-diff-view';

const CONFIG_DIFF_CONTEXT_LINES = 3;
const CONFIG_FILE_NAME = 'config.yaml';

export type ConfigDiffModel = {
  file: FileData | null;
  hunks: HunkData[];
  additions: number;
  deletions: number;
};

export function buildConfigDiff(original: string, modified: string): ConfigDiffModel {
  if (original === modified) {
    return { file: null, hunks: [], additions: 0, deletions: 0 };
  }

  const patch = createTwoFilesPatch(
    CONFIG_FILE_NAME,
    CONFIG_FILE_NAME,
    original,
    modified,
    undefined,
    undefined,
    {
      context: CONFIG_DIFF_CONTEXT_LINES,
      headerOptions: FILE_HEADERS_ONLY,
    }
  );
  const file = parseDiff(patch, { nearbySequences: 'zip' })[0] ?? null;
  const hunks = file?.hunks ?? [];
  let additions = 0;
  let deletions = 0;

  for (const hunk of hunks) {
    for (const change of hunk.changes) {
      if (isInsert(change)) {
        additions++;
      } else if (isDelete(change)) {
        deletions++;
      }
    }
  }

  return { file, hunks, additions, deletions };
}
