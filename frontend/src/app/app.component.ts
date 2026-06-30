import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, finalize, timeout } from 'rxjs';

import { ApiService } from './api.service';
import { FilesWorkspaceComponent } from './files-workspace.component';
import { LoginPanelComponent } from './login-panel.component';
import { FileItem, FileRow } from './models';
import { ShareDialogComponent } from './share-dialog.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [LoginPanelComponent, FilesWorkspaceComponent, ShareDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="shell">
      @if (checkingAuth()) {
        <section class="status-panel view-enter" aria-live="polite">
          <p class="eyebrow">Khrenkov.top</p>
          <h1>Проверка сессии</h1>
        </section>
      } @else if (!authenticated()) {
        <app-login-panel
          [loggingIn]="loggingIn()"
          [error]="error()"
          (loginRequested)="login($event)"
        />
      } @else {
        <app-files-workspace
          [files]="fileRows()"
          [selectedFileName]="selectedFileName()"
          [loadingFiles]="loadingFiles()"
          [syncingFiles]="syncingFiles()"
          [uploading]="uploading()"
          [loggingOut]="loggingOut()"
          [error]="error()"
          [message]="message()"
          (logoutRequested)="logout()"
          (refreshRequested)="loadFiles()"
          (fileSelected)="selectFile($event)"
          (uploadRequested)="upload()"
          (shareRequested)="openShare($event)"
          (deleteRequested)="askDelete($event)"
          (deleteConfirmed)="deleteFile($event)"
          (deleteCancelled)="cancelDelete($event)"
        />
      }

      @if (shareFile(); as file) {
        <app-share-dialog
          [file]="file"
          [count]="shareCount()"
          [url]="shareUrl()"
          [generating]="generatingShare()"
          [error]="shareError()"
          (closeRequested)="closeShare()"
          (countChange)="setShareCount($event)"
          (generateRequested)="generateShare()"
          (copyRequested)="copyShareUrl()"
        />
      }
    </main>
  `
})
export class AppComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fileNameCollator = new Intl.Collator('ru', { numeric: true, sensitivity: 'base' });
  private readonly dateFormatter = new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });

  private fileLoadSub?: Subscription;
  private fileLoadToken = 0;

  readonly checkingAuth = signal(true);
  readonly authenticated = signal(false);
  readonly loggingIn = signal(false);
  readonly loggingOut = signal(false);
  readonly loadingFiles = signal(false);
  readonly syncingFiles = signal(false);
  readonly uploading = signal(false);
  readonly generatingShare = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly shareError = signal('');

  readonly files = signal<readonly FileItem[]>([]);
  readonly selectedFile = signal<File | null>(null);
  readonly shareFile = signal<FileItem | null>(null);
  readonly shareCount = signal(1);
  readonly shareUrl = signal('');
  readonly deletingFiles = signal<ReadonlySet<string>>(new Set<string>());
  readonly confirmingDelete = signal<string | null>(null);

  readonly selectedFileName = computed(() => this.selectedFile()?.name ?? '');

  readonly fileRows = computed<readonly FileRow[]>(() => {
    const deletingFiles = this.deletingFiles();
    const confirmingDelete = this.confirmingDelete();

    return this.files().map(file => ({
      ...file,
      sizeLabel: this.formatBytes(file.size),
      modifiedLabel: this.formatDate(file.modified),
      downloadHref: this.downloadUrl(file.name),
      deleting: deletingFiles.has(file.name),
      confirmingDelete: confirmingDelete === file.name
    }));
  });

  ngOnInit(): void {
    this.api.status().pipe(
      timeout({ first: 8000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.checkingAuth.set(false))
    ).subscribe({
      next: status => {
        this.authenticated.set(status.authenticated);
        if (status.authenticated) {
          this.refreshFiles({ visible: true, reportErrors: true });
        } else {
          this.resetWorkspaceState();
        }
      },
      error: () => {
        this.authenticated.set(false);
        this.resetWorkspaceState();
      }
    });
  }

  login(password: string): void {
    if (this.loggingIn()) {
      return;
    }

    this.clearFeedback();
    this.loggingIn.set(true);

    this.api.login(password).pipe(
      timeout({ first: 10000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loggingIn.set(false))
    ).subscribe({
      next: () => this.enterWorkspace(),
      error: () => {
        this.authenticated.set(false);
        this.resetWorkspaceState();
        this.error.set('Не удалось войти. Введите пароль заново.');
      }
    });
  }

  logout(): void {
    if (this.loggingOut()) {
      return;
    }

    this.clearFeedback();
    this.loggingOut.set(true);
    this.exitToLogin();

    this.api.logout().pipe(
      timeout({ first: 8000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loggingOut.set(false))
    ).subscribe({
      next: () => {},
      error: () => {
        this.error.set('Сессия сброшена. При необходимости войдите заново.');
      }
    });
  }

  loadFiles(): void {
    this.refreshFiles({ visible: true, reportErrors: true });
  }

  selectFile(file: File | null): void {
    this.selectedFile.set(file);
    this.error.set('');
  }

  upload(): void {
    const file = this.selectedFile();
    if (!file || this.uploading()) {
      return;
    }

    this.clearFeedback();
    this.uploading.set(true);

    this.api.upload(file).pipe(
      timeout({ first: 300000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.uploading.set(false))
    ).subscribe({
      next: uploadedFile => {
        this.files.set(this.upsertFile(uploadedFile));
        this.selectedFile.set(null);
        this.message.set('Файл загружен.');
        this.refreshFiles({ visible: false, reportErrors: false });
      },
      error: () => {
        this.error.set('Не удалось загрузить файл.');
      }
    });
  }

  askDelete(file: FileItem): void {
    if (this.deletingFiles().has(file.name)) {
      return;
    }

    this.confirmingDelete.set(file.name);
  }

  cancelDelete(fileName: string): void {
    if (this.confirmingDelete() === fileName) {
      this.confirmingDelete.set(null);
    }
  }

  deleteFile(file: FileItem): void {
    if (this.deletingFiles().has(file.name)) {
      return;
    }

    this.clearFeedback();
    this.confirmingDelete.set(null);
    this.setDeleting(file.name, true);

    this.api.deleteFile(file.name).pipe(
      timeout({ first: 10000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.setDeleting(file.name, false))
    ).subscribe({
      next: () => {
        this.files.set(this.files().filter(item => item.name !== file.name));
        if (this.shareFile()?.name === file.name) {
          this.closeShare();
        }
        this.message.set('Файл удален.');
        this.refreshFiles({ visible: false, reportErrors: false });
      },
      error: () => {
        this.error.set(`Не удалось удалить ${file.name}.`);
      }
    });
  }

  openShare(file: FileItem): void {
    if (this.deletingFiles().has(file.name)) {
      return;
    }

    this.shareFile.set(file);
    this.shareCount.set(1);
    this.shareUrl.set('');
    this.shareError.set('');
    this.error.set('');
  }

  closeShare(): void {
    this.shareFile.set(null);
    this.shareUrl.set('');
    this.shareError.set('');
  }

  setShareCount(count: number): void {
    this.shareCount.set(Math.max(1, Math.floor(Number(count) || 1)));
  }

  generateShare(): void {
    const file = this.shareFile();
    if (!file || this.generatingShare()) {
      return;
    }

    const downloads = Math.max(1, Math.floor(Number(this.shareCount()) || 1));
    this.shareCount.set(downloads);
    this.shareUrl.set('');
    this.shareError.set('');
    this.generatingShare.set(true);

    this.api.createShare(file.name, downloads).pipe(
      timeout({ first: 10000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.generatingShare.set(false))
    ).subscribe({
      next: response => {
        this.shareUrl.set(`${window.location.origin}/share/${response.token}`);
      },
      error: () => {
        this.shareError.set('Не удалось создать ссылку.');
      }
    });
  }

  copyShareUrl(): void {
    const url = this.shareUrl();
    if (!url) {
      return;
    }

    void this.writeClipboard(url).then(
      () => {
        this.message.set('Ссылка скопирована.');
        this.shareError.set('');
      },
      () => {
        this.shareError.set('Не удалось скопировать ссылку.');
      }
    );
  }

  private enterWorkspace(): void {
    this.authenticated.set(true);
    this.resetWorkspaceState();
    this.refreshFiles({ visible: true, reportErrors: true });
  }

  private exitToLogin(): void {
    this.authenticated.set(false);
    this.cancelFileRefresh();
    this.resetWorkspaceState();
  }

  private refreshFiles(options: { visible: boolean; reportErrors: boolean }): void {
    const token = ++this.fileLoadToken;
    this.fileLoadSub?.unsubscribe();

    if (options.visible) {
      this.loadingFiles.set(true);
      this.syncingFiles.set(false);
      if (options.reportErrors) {
        this.error.set('');
      }
    } else {
      this.syncingFiles.set(true);
    }

    this.fileLoadSub = this.api.listFiles().pipe(
      timeout({ first: 10000 }),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => {
        if (token !== this.fileLoadToken) {
          return;
        }

        this.loadingFiles.set(false);
        this.syncingFiles.set(false);
        this.fileLoadSub = undefined;
      })
    ).subscribe({
      next: files => {
        if (token !== this.fileLoadToken) {
          return;
        }

        this.authenticated.set(true);
        this.files.set(this.normalizeFiles(files));
      },
      error: () => {
        if (token !== this.fileLoadToken || !options.reportErrors) {
          return;
        }

        this.authenticated.set(false);
        this.resetWorkspaceState();
        this.error.set('Не удалось загрузить список файлов. Введите пароль заново.');
      }
    });
  }

  private cancelFileRefresh(): void {
    this.fileLoadToken++;
    this.fileLoadSub?.unsubscribe();
    this.fileLoadSub = undefined;
    this.loadingFiles.set(false);
    this.syncingFiles.set(false);
  }

  private resetWorkspaceState(): void {
    this.files.set([]);
    this.selectedFile.set(null);
    this.shareFile.set(null);
    this.shareCount.set(1);
    this.shareUrl.set('');
    this.shareError.set('');
    this.confirmingDelete.set(null);
    this.deletingFiles.set(new Set<string>());
  }

  private clearFeedback(): void {
    this.error.set('');
    this.message.set('');
    this.shareError.set('');
  }

  private setDeleting(fileName: string, active: boolean): void {
    const next = new Set(this.deletingFiles());
    if (active) {
      next.add(fileName);
    } else {
      next.delete(fileName);
    }
    this.deletingFiles.set(next);
  }

  private upsertFile(file: FileItem): readonly FileItem[] {
    return this.normalizeFiles([
      ...this.files().filter(item => item.name !== file.name),
      file
    ]);
  }

  private normalizeFiles(files: readonly FileItem[]): readonly FileItem[] {
    return [...files].sort((left, right) => this.fileNameCollator.compare(left.name, right.name));
  }

  private downloadUrl(fileName: string): string {
    return `/api/files/download/${encodeURIComponent(fileName)}`;
  }

  private formatDate(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '-' : this.dateFormatter.format(date);
  }

  private formatBytes(size: number): string {
    if (size < 1024) {
      return `${size} Б`;
    }

    const units = ['КБ', 'МБ', 'ГБ', 'ТБ'];
    let value = size / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }

  private async writeClipboard(text: string): Promise<void> {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return;
    }

    const field = document.createElement('textarea');
    field.value = text;
    field.setAttribute('readonly', '');
    field.style.position = 'fixed';
    field.style.left = '-9999px';
    document.body.appendChild(field);
    field.select();

    try {
      if (!document.execCommand('copy')) {
        throw new Error('copy failed');
      }
    } finally {
      document.body.removeChild(field);
    }
  }
}
