import { saveAs } from 'file-saver';

export type DownloadBlobOptions = {
  filename: string;
  blob: Blob;
  revokeDelayMs?: number;
};

export function downloadBlob({ filename, blob }: DownloadBlobOptions) {
  saveAs(blob, filename);
}
