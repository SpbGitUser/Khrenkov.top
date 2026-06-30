export interface AuthStatus {
  authenticated: boolean;
}

export interface FileItem {
  name: string;
  size: number;
  modified: string;
}

export interface ShareResponse {
  token: string;
}

export interface FileRow extends FileItem {
  sizeLabel: string;
  modifiedLabel: string;
  downloadHref: string;
  deleting: boolean;
  confirmingDelete: boolean;
}
