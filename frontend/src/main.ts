import { CommonModule } from '@angular/common';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { Component, ElementRef, Injectable, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { bootstrapApplication } from '@angular/platform-browser';
import { finalize, Observable, timeout } from 'rxjs';

interface AuthStatus {
  authenticated: boolean;
}

interface FileItem {
  name: string;
  size: number;
  modified: string;
}

interface ShareResponse {
  token: string;
}

@Injectable({ providedIn: 'root' })
class ApiService {
  private readonly http = inject(HttpClient);

  status(): Observable<AuthStatus> {
    return this.http.get<AuthStatus>('/api/auth/status', { withCredentials: true });
  }

  login(password: string): Observable<AuthStatus> {
    return this.http.post<AuthStatus>('/api/auth/login', { password }, { withCredentials: true });
  }

  logout(): Observable<AuthStatus> {
    return this.http.post<AuthStatus>('/api/auth/logout', {}, { withCredentials: true });
  }

  listFiles(): Observable<FileItem[]> {
    return this.http.get<FileItem[]>('/api/files', { withCredentials: true });
  }

  upload(file: File): Observable<FileItem> {
    const body = new FormData();
    body.append('file', file);
    return this.http.post<FileItem>('/api/files/upload', body, { withCredentials: true });
  }

  deleteFile(fileName: string): Observable<void> {
    return this.http.delete<void>(`/api/files/${encodeURIComponent(fileName)}`, { withCredentials: true });
  }

  createShare(fileName: string, maxDownloads: number): Observable<ShareResponse> {
    return this.http.post<ShareResponse>(
      '/api/files/share',
      { fileName, maxDownloads },
      { withCredentials: true }
    );
  }
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="shell">
      <section *ngIf="checkingStatus" class="login-panel status-panel" aria-live="polite">
        <p class="eyebrow">Khrenkov.top</p>
        <h1>Проверяю доступ</h1>
      </section>

      <section *ngIf="!checkingStatus && !authenticated" class="login-panel" aria-labelledby="login-title">
        <div>
          <p class="eyebrow">Khrenkov.top</p>
          <h1 id="login-title">Вход в файловый менеджер</h1>
        </div>

        <form class="login-form" (ngSubmit)="login()">
          <label for="password">Пароль</label>
          <input
            id="password"
            name="password"
            type="password"
            autocomplete="current-password"
            [(ngModel)]="password"
            [disabled]="loggingIn"
            autofocus
            required
          />
          <button type="submit" [disabled]="loggingIn || !password.trim()">
            {{ loggingIn ? 'Проверяю...' : 'Войти' }}
          </button>
          <p *ngIf="error" class="error">{{ error }}</p>
        </form>
      </section>

      <section *ngIf="!checkingStatus && authenticated" class="workspace" aria-labelledby="files-title">
        <header class="topbar">
          <div>
            <p class="eyebrow">Khrenkov.top</p>
            <h1 id="files-title">Файлы</h1>
          </div>
          <button class="ghost-button" type="button" (click)="logout()" [disabled]="loggingOut">
            {{ loggingOut ? 'Выхожу...' : 'Выйти' }}
          </button>
        </header>

        <div class="toolbar">
          <label class="upload-control">
            <input #fileInput type="file" (change)="onFileSelected($event)" [disabled]="uploading" />
            <span>{{ selectedFile?.name || 'Выбрать файл' }}</span>
          </label>
          <button type="button" (click)="upload()" [disabled]="uploading || !selectedFile">
            {{ uploading ? 'Загружаю...' : 'Загрузить' }}
          </button>
          <button class="ghost-button" type="button" (click)="loadFiles()" [disabled]="loadingFiles">
            {{ loadingFiles ? 'Обновляю...' : 'Обновить' }}
          </button>
          <span *ngIf="syncingFiles && !loadingFiles" class="sync-indicator">Сверяю список...</span>
        </div>

        <p *ngIf="error" class="error">{{ error }}</p>
        <p *ngIf="message" class="message">{{ message }}</p>

        <div class="table-wrap">
          <table *ngIf="files.length; else emptyState">
            <thead>
              <tr>
                <th>Имя файла</th>
                <th>Размер</th>
                <th>Изменён</th>
                <th class="actions">Действия</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let file of files; trackBy: trackFile">
                <td class="file-name">{{ file.name }}</td>
                <td>{{ formatBytes(file.size) }}</td>
                <td>{{ file.modified | date: 'dd.MM.yyyy HH:mm' }}</td>
                <td class="actions">
                  <a class="icon-button" [href]="downloadUrl(file.name)">Скачать</a>
                  <button class="icon-button" type="button" (click)="openShare(file)" [disabled]="isDeleting(file.name)">
                    Ссылка
                  </button>
                  <button class="danger-button" type="button" (click)="deleteFile(file)" [disabled]="isDeleting(file.name)">
                    {{ isDeleting(file.name) ? 'Удаляю...' : 'Удалить' }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <ng-template #emptyState>
          <div class="empty-state">{{ loadingFiles ? 'Загружаю список...' : 'Нет загруженных файлов.' }}</div>
        </ng-template>
      </section>

      <div *ngIf="shareFile" class="modal-backdrop" (click)="closeShare()">
        <section class="modal" (click)="$event.stopPropagation()" aria-labelledby="share-title">
          <header>
            <h2 id="share-title">Выдать ссылку</h2>
            <button class="close-button" type="button" (click)="closeShare()" aria-label="Закрыть">x</button>
          </header>

          <p class="share-file">{{ shareFile.name }}</p>
          <label for="shareCount">Количество скачиваний</label>
          <div class="stepper">
            <button type="button" (click)="decrementShareCount()" [disabled]="generatingShare">-</button>
            <input id="shareCount" type="number" min="1" [(ngModel)]="shareCount" [disabled]="generatingShare" />
            <button type="button" (click)="shareCount = shareCount + 1" [disabled]="generatingShare">+</button>
          </div>

          <button type="button" (click)="generateShare()" [disabled]="generatingShare || shareCount < 1">
            {{ generatingShare ? 'Создаю...' : 'Сгенерировать' }}
          </button>

          <div *ngIf="shareUrl" class="share-result">
            <input type="text" [value]="shareUrl" readonly />
            <button type="button" (click)="copyShareUrl()">Копировать</button>
          </div>
        </section>
      </div>
    </main>
  `
})
class AppComponent implements OnInit {
  private readonly api = inject(ApiService);

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  authenticated = false;
  checkingStatus = true;
  loggingIn = false;
  loggingOut = false;
  loadingFiles = false;
  syncingFiles = false;
  uploading = false;
  generatingShare = false;
  password = '';
  files: FileItem[] = [];
  selectedFile: File | null = null;
  shareFile: FileItem | null = null;
  shareCount = 1;
  shareUrl = '';
  error = '';
  message = '';
  deletingFiles = new Set<string>();

  private filesRequestId = 0;
  private activeVisibleFileLoads = 0;
  private activeSilentFileLoads = 0;

  ngOnInit(): void {
    this.api.status().pipe(
      timeout({ first: 8000 }),
      finalize(() => {
        this.checkingStatus = false;
      })
    ).subscribe({
      next: status => {
        this.authenticated = status.authenticated;
        if (status.authenticated) {
          this.loadFiles();
        }
      },
      error: () => {
        this.authenticated = false;
        this.error = 'Не удалось подключиться к серверу';
      }
    });
  }

  login(): void {
    const password = this.password.trim();
    if (!password || this.loggingIn) {
      return;
    }

    this.clearFeedback();
    this.loggingIn = true;
    this.api.login(password).pipe(
      finalize(() => {
        this.loggingIn = false;
      })
    ).subscribe({
      next: () => {
        this.authenticated = true;
        this.password = '';
        this.resetWorkspaceState();
        this.loadFiles();
      },
      error: () => {
        this.error = 'Неверный пароль';
      }
    });
  }

  logout(): void {
    if (this.loggingOut) {
      return;
    }

    this.clearFeedback();
    this.loggingOut = true;
    this.api.logout().pipe(
      finalize(() => {
        this.loggingOut = false;
      })
    ).subscribe({
      next: () => {
        this.authenticated = false;
        this.resetWorkspaceState();
      },
      error: () => {
        this.error = 'Не удалось выйти';
      }
    });
  }

  loadFiles(): void {
    this.refreshFiles({ visible: true, reportErrors: true });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.item(0) ?? null;
    this.error = '';
  }

  upload(): void {
    if (!this.selectedFile || this.uploading) {
      return;
    }

    const file = this.selectedFile;
    this.clearFeedback();
    this.uploading = true;
    this.api.upload(file).pipe(
      finalize(() => {
        this.uploading = false;
      })
    ).subscribe({
      next: uploadedFile => {
        this.invalidatePendingFileLoads();
        this.upsertFile(uploadedFile);
        this.selectedFile = null;
        this.resetFileInput();
        this.message = 'Файл загружен';
        this.refreshFiles({ visible: false, reportErrors: false });
      },
      error: () => {
        this.error = 'Не удалось загрузить файл';
      }
    });
  }

  deleteFile(file: FileItem): void {
    if (this.isDeleting(file.name) || !confirm(`Удалить ${file.name}?`)) {
      return;
    }

    this.clearFeedback();
    this.setDeleting(file.name, true);
    this.api.deleteFile(file.name).pipe(
      finalize(() => {
        this.setDeleting(file.name, false);
      })
    ).subscribe({
      next: () => {
        this.invalidatePendingFileLoads();
        this.files = this.files.filter(item => item.name !== file.name);
        if (this.shareFile?.name === file.name) {
          this.closeShare();
        }
        this.message = 'Файл удалён';
        this.refreshFiles({ visible: false, reportErrors: false });
      },
      error: () => {
        this.error = `Не удалось удалить ${file.name}`;
      }
    });
  }

  openShare(file: FileItem): void {
    if (this.isDeleting(file.name)) {
      return;
    }

    this.shareFile = file;
    this.shareCount = 1;
    this.shareUrl = '';
    this.error = '';
  }

  closeShare(): void {
    this.shareFile = null;
    this.shareUrl = '';
  }

  decrementShareCount(): void {
    this.shareCount = Math.max(1, this.shareCount - 1);
  }

  generateShare(): void {
    if (!this.shareFile || this.generatingShare) {
      return;
    }

    const downloads = Math.max(1, Math.floor(Number(this.shareCount) || 1));
    this.shareCount = downloads;
    this.clearFeedback();
    this.generatingShare = true;
    this.api.createShare(this.shareFile.name, downloads).pipe(
      finalize(() => {
        this.generatingShare = false;
      })
    ).subscribe({
      next: response => {
        this.shareUrl = `${window.location.origin}/share/${response.token}`;
      },
      error: () => {
        this.error = 'Не удалось создать ссылку';
      }
    });
  }

  copyShareUrl(): void {
    if (!this.shareUrl) {
      return;
    }

    void navigator.clipboard.writeText(this.shareUrl).then(
      () => {
        this.message = 'Ссылка скопирована';
      },
      () => {
        this.error = 'Не удалось скопировать ссылку';
      }
    );
  }

  downloadUrl(fileName: string): string {
    return `/api/files/download/${encodeURIComponent(fileName)}`;
  }

  formatBytes(size: number): string {
    if (size < 1024) {
      return `${size} Б`;
    }

    const units = ['КБ', 'МБ', 'ГБ'];
    let value = size / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  }

  trackFile(_index: number, file: FileItem): string {
    return file.name;
  }

  isDeleting(fileName: string): boolean {
    return this.deletingFiles.has(fileName);
  }

  private refreshFiles(options: { visible: boolean; reportErrors: boolean }): void {
    const requestId = ++this.filesRequestId;

    if (options.visible) {
      this.activeVisibleFileLoads++;
      this.loadingFiles = true;
      this.error = '';
    } else {
      this.activeSilentFileLoads++;
      this.syncingFiles = true;
    }

    this.api.listFiles().pipe(
      finalize(() => {
        if (options.visible) {
          this.activeVisibleFileLoads = Math.max(0, this.activeVisibleFileLoads - 1);
          this.loadingFiles = this.activeVisibleFileLoads > 0;
        } else {
          this.activeSilentFileLoads = Math.max(0, this.activeSilentFileLoads - 1);
          this.syncingFiles = this.activeSilentFileLoads > 0;
        }
      })
    ).subscribe({
      next: files => {
        if (requestId === this.filesRequestId) {
          this.files = this.normalizeFiles(files);
        }
      },
      error: () => {
        if (requestId === this.filesRequestId && options.reportErrors) {
          this.error = 'Не удалось загрузить список файлов';
        }
      }
    });
  }

  private invalidatePendingFileLoads(): void {
    this.filesRequestId++;
  }

  private upsertFile(file: FileItem): void {
    this.files = this.normalizeFiles([
      ...this.files.filter(item => item.name !== file.name),
      file
    ]);
  }

  private normalizeFiles(files: FileItem[]): FileItem[] {
    return [...files].sort((left, right) => left.name.localeCompare(right.name, 'ru'));
  }

  private setDeleting(fileName: string, active: boolean): void {
    const next = new Set(this.deletingFiles);
    if (active) {
      next.add(fileName);
    } else {
      next.delete(fileName);
    }
    this.deletingFiles = next;
  }

  private resetWorkspaceState(): void {
    this.files = [];
    this.selectedFile = null;
    this.shareFile = null;
    this.shareCount = 1;
    this.shareUrl = '';
    this.deletingFiles = new Set<string>();
    this.invalidatePendingFileLoads();
    this.resetFileInput();
  }

  private resetFileInput(): void {
    if (this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  private clearFeedback(): void {
    this.error = '';
    this.message = '';
  }
}

bootstrapApplication(AppComponent, {
  providers: [provideHttpClient()]
}).catch(error => console.error(error));
